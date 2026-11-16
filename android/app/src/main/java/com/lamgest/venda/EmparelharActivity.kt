package com.lamgest.venda

import android.net.Uri
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import kotlin.concurrent.thread

/** Liga a app ao posto: QR code ou endereço e código mostrados em Configurações → Postos. */
class EmparelharActivity : ComponentActivity() {
    private lateinit var config: Config
    private lateinit var servidor: EditText
    private lateinit var codigo: EditText
    private lateinit var estado: TextView
    private lateinit var ligar: Button
    private lateinit var gestor: CheckBox

    private val leitor = registerForActivityResult(ScanContract()) { resultado ->
        val lido = resultado.contents ?: return@registerForActivityResult
        val uri = Uri.parse(lido)
        if (uri.scheme != "lamgest") {
            estado.text = "Este QR code não é o de emparelhamento do LAMGest."
            return@registerForActivityResult
        }
        servidor.setText(uri.getQueryParameter("servidor") ?: "")
        codigo.setText(uri.getQueryParameter("codigo") ?: "")
        emparelhar()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        config = Config(this)
        val painel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(48, 64, 48, 48)
        }
        painel.addView(TextView(this).apply { text = "LAMGest Venda"; textSize = 26f; gravity = Gravity.CENTER })
        painel.addView(TextView(this).apply {
            text = if (config.posto.isEmpty()) "No computador: Configurações → Postos → Ligar tablets a este posto." else "Ligado ao posto ${config.posto}."
            textSize = 15f
            gravity = Gravity.CENTER
            setPadding(0, 16, 0, 32)
        })
        painel.addView(Button(this).apply {
            text = "Ler QR code"
            setOnClickListener {
                leitor.launch(ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("Aponte para o QR code do posto").setBeepEnabled(false).setOrientationLocked(true))
            }
        })
        servidor = EditText(this).apply {
            hint = "Servidor (ex.: 192.168.1.10:5000)"
            inputType = InputType.TYPE_TEXT_VARIATION_URI
            setText(config.servidor)
        }
        codigo = EditText(this).apply {
            hint = "Código (ex.: ABCD-EFGH)"
            inputType = InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS
        }
        painel.addView(servidor)
        painel.addView(codigo)
        gestor = CheckBox(this).apply {
            text = "Aparelho de gestor (ver todas as lojas ligadas)"
            isChecked = config.gestor
            setPadding(0, 16, 0, 16)
        }
        painel.addView(gestor)
        ligar = Button(this).apply { text = "Ligar"; setOnClickListener { emparelhar() } }
        painel.addView(ligar)
        if (config.lojas.size > 1) painel.addView(TextView(this).apply {
            text = "Lojas neste aparelho: " + config.lojas.joinToString(", ") { it.nome } +
                "\nPara acrescentar outra loja, emparelhe aqui no Wi-Fi dessa loja (o servidor e o código dela)."
            textSize = 14f
            setPadding(0, 24, 0, 0)
        })
        estado = TextView(this).apply { textSize = 16f; gravity = Gravity.CENTER; setPadding(0, 24, 0, 0) }
        painel.addView(estado)
        setContentView(painel)
    }

    private fun normalizar(texto: String): String {
        val limpo = texto.trim().trimEnd('/')
        return if (limpo.startsWith("http://") || limpo.startsWith("https://")) limpo else "http://$limpo"
    }

    private fun emparelhar() {
        val endereco = normalizar(servidor.text.toString())
        val codigoLido = codigo.text.toString().trim()
        if (endereco == "http://" || codigoLido.isEmpty()) {
            estado.text = "Indique o servidor e o código."
            return
        }
        ligar.isEnabled = false
        estado.text = "A ligar…"
        thread {
            val resultado = runCatching { pedir(endereco, codigoLido) to identidade(endereco) }
            runOnUiThread {
                ligar.isEnabled = true
                resultado.onSuccess { (posto, loja) ->
                    // A loja fica guardada pelo identificador do servidor (servidores antigos: pelo endereço).
                    val id = loja?.optString("identificador")?.takeIf { it.isNotEmpty() } ?: endereco
                    val anterior = config.lojas.firstOrNull { it.id == id || it.servidor == endereco }
                    config.gestor = gestor.isChecked
                    config.guardarLoja(Config.Loja(id, loja?.optString("nome")?.takeIf { it.isNotEmpty() } ?: posto.getString("nome"), endereco,
                        anterior?.servidorExterno ?: "", posto.getInt("terminalId"), posto.getString("nome")), tornarActual = true)
                    finish()
                }.onFailure { estado.text = it.message ?: "Não foi possível ligar ao servidor." }
            }
        }
    }

    /** Identidade da loja (identificador, nome); null nos servidores que ainda não a têm. */
    private fun identidade(endereco: String): JSONObject? = runCatching {
        val ligacao = URL("$endereco/api/loja").openConnection() as HttpURLConnection
        try {
            ligacao.connectTimeout = 5000
            ligacao.readTimeout = 5000
            if (ligacao.responseCode in 200..299) JSONObject(ligacao.inputStream.bufferedReader().use { it.readText() }) else null
        } finally {
            ligacao.disconnect()
        }
    }.getOrNull()

    private fun pedir(endereco: String, codigoLido: String): JSONObject {
        val ligacao = URL("$endereco/api/tablets/emparelhar").openConnection() as HttpURLConnection
        try {
            ligacao.requestMethod = "POST"
            ligacao.connectTimeout = 8000
            ligacao.readTimeout = 8000
            ligacao.doOutput = true
            ligacao.setRequestProperty("Content-Type", "application/json")
            ligacao.outputStream.use { it.write(JSONObject().put("codigo", codigoLido).toString().toByteArray()) }
            val estadoHttp = ligacao.responseCode
            val corpo = (if (estadoHttp in 200..299) ligacao.inputStream else ligacao.errorStream)?.bufferedReader()?.use { it.readText() } ?: ""
            if (estadoHttp in 200..299) return JSONObject(corpo)
            throw IllegalStateException(runCatching { JSONObject(corpo).getString("detail") }.getOrDefault("O servidor recusou a ligação ($estadoHttp)."))
        } catch (e: java.io.IOException) {
            throw IllegalStateException("Não foi possível contactar o servidor. Verifique o endereço e a rede Wi-Fi.")
        } finally {
            ligacao.disconnect()
        }
    }
}
