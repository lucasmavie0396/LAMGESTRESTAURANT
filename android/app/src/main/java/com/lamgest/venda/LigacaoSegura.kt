package com.lamgest.venda

import android.net.http.SslCertificate
import android.net.http.SslError
import android.util.Base64
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.net.HttpURLConnection
import java.net.URL
import java.security.KeyStore
import java.security.MessageDigest
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.X509TrustManager

/**
 * HTTPS na rede local: cada servidor LAMGest tem a sua autoridade de certificação («LAMGest Restaurant - autoridade local»),
 * que o servidor publica em /api/sistema/certificado. A app guarda-a por loja e só aceita certificados assinados por ela
 * (nunca os de outra loja nem os de um servidor falso na rede). Sem autoridade guardada, a loja continua em HTTP.
 */
object LigacaoSegura {
    /** Autoridade local de uma loja: [porta] HTTPS e o certificado em Base64 (DER). */
    data class Autoridade(val porta: Int, val certificado: String)

    private fun ler(base64: String): X509Certificate? = runCatching {
        CertificateFactory.getInstance("X.509")
            .generateCertificate(ByteArrayInputStream(Base64.decode(base64, Base64.DEFAULT))) as X509Certificate
    }.getOrNull()

    private fun sha256(dados: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(dados).joinToString("") { "%02X".format(it) }

    /** Abre o endereço: em HTTPS, só com a autoridade da loja (ou falha, se a loja ainda não a tiver). */
    fun abrir(url: String, certificado: String): HttpURLConnection {
        val ligacao = URL(url).openConnection() as HttpURLConnection
        if (ligacao is HttpsURLConnection) {
            val ca = ler(certificado)
            if (ca != null) ligacao.sslSocketFactory = contexto(ca).socketFactory
        }
        return ligacao
    }

    private fun contexto(ca: X509Certificate): SSLContext {
        val loja = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null, null)
            setCertificateEntry("lamgest", ca)
        }
        val tmf = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(loja) }
        return SSLContext.getInstance("TLS").apply { init(null, tmf.trustManagers, null) }
    }

    /**
     * Pergunta ao servidor (em [base], HTTP ou HTTPS) a autoridade local. Em HTTPS ainda sem autoridade guardada, aceita o
     * certificado só nesta leitura (é o primeiro contacto, como o emparelhamento em HTTP); a partir daí só vale a autoridade lida.
     * Null se o servidor não tiver HTTPS ou for de uma versão anterior.
     */
    fun descobrir(base: String): Autoridade? = runCatching {
        val ligacao = URL("$base/api/sistema/certificado").openConnection() as HttpURLConnection
        if (ligacao is HttpsURLConnection) {
            val primeiroContacto = object : X509TrustManager {
                override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {}
                override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {}
                override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            }
            ligacao.sslSocketFactory = SSLContext.getInstance("TLS").apply { init(null, arrayOf(primeiroContacto), null) }.socketFactory
            ligacao.hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true }
        }
        try {
            ligacao.connectTimeout = 4000
            ligacao.readTimeout = 4000
            if (ligacao.responseCode !in 200..299) return@runCatching null
            val d = JSONObject(ligacao.inputStream.bufferedReader().use { it.readText() })
            if (!d.optBoolean("https", false)) return@runCatching null
            val certificado = d.getString("certificado")
            val ca = ler(certificado) ?: return@runCatching null
            if (!sha256(ca.encoded).equals(d.optString("impressaoDigital"), ignoreCase = true)) return@runCatching null
            Autoridade(d.getInt("porta"), certificado)
        } finally {
            ligacao.disconnect()
        }
    }.getOrNull()

    /** Endereço HTTPS do mesmo servidor: https://anfitrião:porta-https. */
    fun enderecoHttps(base: String, porta: Int): String {
        val u = URL(base)
        return "https://${u.host}:$porta"
    }

    /**
     * WebView: certificado do servidor que o Android não conhece. Aceita-se só se for assinado pela autoridade local da loja
     * e estiver dentro da validade.
     */
    fun aceita(erro: SslError, certificado: String): Boolean {
        if (certificado.isEmpty()) return false
        val ca = ler(certificado) ?: return false
        val bytes = SslCertificate.saveState(erro.certificate)?.getByteArray("x509-certificate") ?: return false
        return runCatching {
            val folha = CertificateFactory.getInstance("X.509").generateCertificate(ByteArrayInputStream(bytes)) as X509Certificate
            folha.checkValidity()
            folha.verify(ca.publicKey)
            true
        }.getOrDefault(false)
    }
}
