package com.lamgest.venda

import android.annotation.SuppressLint
import android.app.AlertDialog
import androidx.activity.ComponentActivity
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import org.json.JSONObject
import android.content.Intent
import android.graphics.Color
import android.net.Uri
import android.net.http.SslError
import android.os.Bundle
import android.view.Gravity
import android.webkit.CookieManager
import android.webkit.SslErrorHandler
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import kotlin.concurrent.thread

/** Abre o ecrã de venda do servidor. Sem ligação não há venda: mostra o erro e permite tentar de novo. */
class MainActivity : ComponentActivity() {
    private lateinit var config: Config
    private lateinit var impressora: ImpressoraSunmi
    private var web: WebView? = null

    // Leitura pela câmara (QR code ou código de barras) pedida pela página; o texto lido volta no evento «lamgest-codigo».
    private val leitor = registerForActivityResult(ScanContract()) { resultado ->
        val texto = resultado.contents ?: return@registerForActivityResult
        web?.evaluateJavascript("window.dispatchEvent(new CustomEvent('lamgest-codigo', { detail: ${JSONObject.quote(texto)} }))", null)
    }

    private fun lerComCamara() = leitor.launch(
        ScanOptions().setPrompt("Aponte para o QR code ou o código de barras do cartão").setBeepEnabled(true).setOrientationLocked(true),
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        config = Config(this)
        impressora = ImpressoraSunmi(applicationContext)
        impressora.ligar { runOnUiThread { arrancar() } }
    }

    override fun onDestroy() {
        impressora.desligar()
        web?.destroy()
        super.onDestroy()
    }

    override fun onRestart() {
        super.onRestart()
        // Só ao voltar do emparelhamento (pode ter mudado de servidor); ao voltar da câmara a venda continua onde estava.
        if (aEmparelhar) {
            aEmparelhar = false
            arrancar()
        }
    }

    private fun arrancar() {
        if (!config.emparelhado) {
            abrirEmparelhamento()
            return
        }
        val versao = Regex("Chrome/(\\d+)").find(WebSettings.getDefaultUserAgent(this))?.groupValues?.get(1)?.toIntOrNull() ?: 0
        if (versao in 1 until VERSAO_MINIMA) {
            mostrarMensagem("O componente Android System WebView deste PDA é demasiado antigo (versão $versao). Actualize-o para a versão $VERSAO_MINIMA ou mais recente.")
            return
        }
        mostrarMensagem("A ligar ao servidor…", botoes = false)
        thread {
            val base = escolherServidor()
            runOnUiThread {
                val loja = config.lojaActual?.nome?.let { " ($it)" } ?: ""
                if (base == null) {
                    mostrarMensagem(
                        if (config.servidorExterno.isEmpty()) "Não foi possível ligar ao servidor$loja ${config.servidor}. Verifique a rede Wi-Fi."
                        else "Não foi possível ligar ao servidor$loja, nem pela rede da loja nem pela VPN. Verifique o Wi-Fi ou os dados móveis e a VPN.",
                    )
                } else {
                    if (base != config.servidor) Toast.makeText(this, "Ligado pela VPN$loja", Toast.LENGTH_SHORT).show()
                    abrirVenda(base)
                }
            }
        }
    }

    /**
     * Rede do restaurante primeiro (Wi-Fi); se não responder e houver endereço externo, a VPN pelos dados móveis.
     * Na rede do restaurante, actualiza o endereço externo que o servidor indica (vazio = acesso externo desligado).
     */
    private fun escolherServidor(): String? {
        LamgestApp.prenderAoWifi(true)
        passarParaHttps()
        // Lojas recebidas de outra loja (aparelho de gestor) só têm o endereço da VPN.
        if (config.servidor.isNotEmpty() && responde(config.servidor, 3000)) {
            actualizarEnderecoExterno()
            if (config.gestor) importarLojas(config.servidor)
            return config.servidor
        }
        val externo = config.servidorExterno
        if (externo.isEmpty()) return null
        LamgestApp.prenderAoWifi(false)
        if (responde(externo, 8000)) {
            if (config.gestor) importarLojas(externo)
            return externo
        }
        LamgestApp.prenderAoWifi(true)
        return null
    }

    /**
     * Loja em HTTP cujo servidor já tem HTTPS: guarda a autoridade local e passa a usar https://servidor:porta-https.
     * Uma loja já em HTTPS nunca troca de autoridade sozinha (um servidor falso na rede não a pode substituir): se o servidor
     * mudar de autoridade, emparelha-se de novo em «Ligação ao servidor».
     */
    private fun passarParaHttps() {
        val loja = config.lojaActual ?: return
        if (!loja.servidor.startsWith("http://")) return
        val autoridade = LigacaoSegura.descobrir(loja.servidor) ?: return
        val https = LigacaoSegura.enderecoHttps(loja.servidor, autoridade.porta)
        if (responde(https, 3000, autoridade.certificado))
            config.guardarLoja(loja.copy(servidor = https, certificado = autoridade.certificado), tornarActual = true)
    }

    /** Aparelho de gestor: guarda as outras lojas que o servidor conhece (Configurações → Lojas), pelo endereço da VPN. */
    private fun importarLojas(base: String) {
        runCatching {
            val ligacao = LigacaoSegura.abrir("$base/api/lojas", config.certificado)
            try {
                ligacao.connectTimeout = 4000
                ligacao.readTimeout = 4000
                if (ligacao.responseCode !in 200..299) return
                val outras = JSONObject(ligacao.inputStream.bufferedReader().use { it.readText() }).getJSONArray("outras")
                for (i in 0 until outras.length()) {
                    val o = outras.getJSONObject(i)
                    val id = o.getString("identificador")
                    val conhecida = config.lojas.firstOrNull { it.id == id }
                    if (conhecida == null)
                        config.guardarLoja(Config.Loja(id, o.getString("nome"), "", o.getString("endereco"), 0, ""), tornarActual = false)
                    else if (conhecida.servidorExterno.isEmpty() || conhecida.nome != o.getString("nome"))
                        config.guardarLoja(conhecida.copy(nome = o.getString("nome"), servidorExterno = conhecida.servidorExterno.ifEmpty { o.getString("endereco") }), tornarActual = false)
                }
            } finally {
                ligacao.disconnect()
            }
        }
    }

    private fun responde(base: String, espera: Int, certificado: String = config.certificado): Boolean = runCatching {
        val ligacao = LigacaoSegura.abrir("$base/api/saude", certificado)
        try {
            ligacao.connectTimeout = espera
            ligacao.readTimeout = espera
            ligacao.responseCode in 200..299
        } finally {
            ligacao.disconnect()
        }
    }.getOrDefault(false)

    private fun actualizarEnderecoExterno() {
        runCatching {
            val ligacao = LigacaoSegura.abrir("${config.servidor}/api/tablets/ligacao", config.certificado)
            try {
                ligacao.connectTimeout = 3000
                ligacao.readTimeout = 3000
                if (ligacao.responseCode !in 200..299) return
                val ligacaoTablets = JSONObject(ligacao.inputStream.bufferedReader().use { it.readText() })
                config.servidorExterno = if (ligacaoTablets.isNull("enderecoExterno")) "" else ligacaoTablets.optString("enderecoExterno", "").trimEnd('/')
            } finally {
                ligacao.disconnect()
            }
        }
    }

    private var aEmparelhar = false

    private fun abrirEmparelhamento() {
        aEmparelhar = true
        startActivity(Intent(this, EmparelharActivity::class.java))
    }

    @SuppressLint("SetJavaScriptEnabled", "AddJavascriptInterface")
    private fun abrirVenda(base: String) {
        val origem = Uri.parse(base)
        val vista = web ?: WebView(this).also { web = it }
        vista.settings.javaScriptEnabled = true
        vista.settings.domStorageEnabled = true
        CookieManager.getInstance().setAcceptCookie(true)
        vista.removeJavascriptInterface("LamgestPDA")
        vista.addJavascriptInterface(PontePda(config, impressora, { runOnUiThread { abrirEmparelhamento() } }, { runOnUiThread { lerComCamara() } },
            { runOnUiThread { arrancar() } }), "LamgestPDA")
        vista.webViewClient = object : WebViewClient() {
            // A ponte da impressora só fica ao alcance das páginas do próprio servidor.
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean =
                !(request.url.host == origem.host && request.url.port == origem.port)

            // HTTPS da loja: só certificados assinados pela autoridade local guardada no emparelhamento.
            @SuppressLint("WebViewClientOnReceivedSslError")
            override fun onReceivedSslError(view: WebView, handler: SslErrorHandler, error: SslError) {
                if (LigacaoSegura.aceita(error, config.certificado)) handler.proceed()
                else {
                    handler.cancel()
                    mostrarMensagem("O certificado de segurança do servidor não é o desta loja. Se o servidor foi reinstalado, ligue o tablet de novo em «Ligação ao servidor».")
                }
            }

            // A rede mudou (ex.: saiu do restaurante): volta a escolher entre a rede local e a VPN.
            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                if (request.isForMainFrame) arrancar()
            }
        }
        setContentView(vista)
        vista.loadUrl("$base/pos")
    }

    /** Lista das lojas guardadas no aparelho; escolher uma liga a app ao servidor dela. */
    private fun escolherLoja() {
        val lojas = config.lojas
        AlertDialog.Builder(this)
            .setTitle("Mudar de loja")
            .setItems(lojas.map { it.nome }.toTypedArray()) { _, i -> if (config.mudarPara(lojas[i].id)) arrancar() }
            .setNegativeButton("Cancelar", null)
            .show()
    }

    private fun mostrarMensagem(texto: String, botoes: Boolean = true) {
        val painel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(48, 48, 48, 48)
            setBackgroundColor(Color.WHITE)
        }
        painel.addView(TextView(this).apply { text = texto; textSize = 18f; gravity = Gravity.CENTER })
        if (botoes) painel.addView(Button(this).apply { text = "Tentar de novo"; setOnClickListener { arrancar() } })
        if (botoes) painel.addView(Button(this).apply { text = "Ligação ao servidor"; setOnClickListener { abrirEmparelhamento() } })
        if (botoes && config.lojas.size > 1) painel.addView(Button(this).apply { text = "Mudar de loja"; setOnClickListener { escolherLoja() } })
        setContentView(painel)
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        val vista = web
        if (vista != null && vista.canGoBack()) vista.goBack() else @Suppress("DEPRECATION") super.onBackPressed()
    }

    private companion object {
        const val VERSAO_MINIMA = 70
    }
}
