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
        abrirVenda()
    }

    private var aEmparelhar = false

    private fun abrirEmparelhamento() {
        aEmparelhar = true
        startActivity(Intent(this, EmparelharActivity::class.java))
    }

    @SuppressLint("SetJavaScriptEnabled", "AddJavascriptInterface")
    private fun abrirVenda() {
        val origem = Uri.parse(config.servidor)
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

            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                if (request.isForMainFrame) mostrarMensagem("Não foi possível ligar ao servidor ${config.servidor}. Verifique a rede Wi-Fi.")
            }
        }
        setContentView(vista)
        vista.loadUrl(config.servidor + "/pos")
    }

    private fun mostrarMensagem(texto: String) {
        val painel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(48, 48, 48, 48)
            setBackgroundColor(Color.WHITE)
        }
        painel.addView(TextView(this).apply { text = texto; textSize = 18f; gravity = Gravity.CENTER })
        painel.addView(Button(this).apply { text = "Tentar de novo"; setOnClickListener { arrancar() } })
        painel.addView(Button(this).apply { text = "Ligação ao servidor"; setOnClickListener { abrirEmparelhamento() } })
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
