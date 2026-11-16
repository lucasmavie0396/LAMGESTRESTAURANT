package com.lamgest.venda

import android.annotation.SuppressLint
import androidx.activity.ComponentActivity
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import org.json.JSONObject
import android.content.Intent
import android.graphics.Color
import android.net.Uri
import android.os.Bundle
import android.view.Gravity
import android.webkit.CookieManager
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import java.net.HttpURLConnection
import java.net.URL
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
                if (base == null) {
                    mostrarMensagem(
                        if (config.servidorExterno.isEmpty()) "Não foi possível ligar ao servidor ${config.servidor}. Verifique a rede Wi-Fi."
                        else "Não foi possível ligar ao servidor, nem pela rede do restaurante nem pela VPN. Verifique o Wi-Fi ou os dados móveis e a VPN.",
                    )
                } else {
                    if (base != config.servidor) Toast.makeText(this, "Ligado fora do restaurante (VPN)", Toast.LENGTH_SHORT).show()
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
        if (responde(config.servidor, 3000)) {
            actualizarEnderecoExterno()
            return config.servidor
        }
        val externo = config.servidorExterno
        if (externo.isEmpty()) return null
        LamgestApp.prenderAoWifi(false)
        if (responde(externo, 8000)) return externo
        LamgestApp.prenderAoWifi(true)
        return null
    }

    private fun responde(base: String, espera: Int): Boolean = runCatching {
        val ligacao = URL("$base/api/saude").openConnection() as HttpURLConnection
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
            val ligacao = URL("${config.servidor}/api/tablets/ligacao").openConnection() as HttpURLConnection
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
        vista.addJavascriptInterface(PontePda(config, impressora, { runOnUiThread { abrirEmparelhamento() } }, { runOnUiThread { lerComCamara() } }), "LamgestPDA")
        vista.webViewClient = object : WebViewClient() {
            // A ponte da impressora só fica ao alcance das páginas do próprio servidor.
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean =
                !(request.url.host == origem.host && request.url.port == origem.port)

            // A rede mudou (ex.: saiu do restaurante): volta a escolher entre a rede local e a VPN.
            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                if (request.isForMainFrame) arrancar()
            }
        }
        setContentView(vista)
        vista.loadUrl("$base/pos")
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
