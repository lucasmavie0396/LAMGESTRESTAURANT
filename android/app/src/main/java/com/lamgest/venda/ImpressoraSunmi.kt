package com.lamgest.venda

import android.content.Context
import android.os.Handler
import android.os.Looper
import com.sunmi.peripheral.printer.InnerPrinterCallback
import com.sunmi.peripheral.printer.InnerPrinterException
import com.sunmi.peripheral.printer.InnerPrinterManager
import com.sunmi.peripheral.printer.SunmiPrinterService
import org.json.JSONArray

/** Impressora interna dos PDA Sunmi (serviço do sistema). Noutros aparelhos fica simplesmente indisponível. */
class ImpressoraSunmi(private val contexto: Context) {
    @Volatile private var servico: SunmiPrinterService? = null
    private var aoLigar: (() -> Unit)? = null

    private val ligacao = object : InnerPrinterCallback() {
        override fun onConnected(service: SunmiPrinterService) {
            servico = service
            concluir()
        }

        override fun onDisconnected() {
            servico = null
        }
    }

    private fun concluir() {
        val accao = aoLigar ?: return
        aoLigar = null
        accao()
    }

    /** Liga ao serviço; [pronto] corre quando ligar ou, se não houver impressora, ao fim de 1,5 s. */
    fun ligar(pronto: () -> Unit) {
        aoLigar = pronto
        val pedido = try {
            InnerPrinterManager.getInstance().bindService(contexto, ligacao)
        } catch (e: InnerPrinterException) {
            false
        }
        if (pedido) Handler(Looper.getMainLooper()).postDelayed({ concluir() }, 1500) else concluir()
    }

    fun desligar() {
        try {
            InnerPrinterManager.getInstance().unBindService(contexto, ligacao)
        } catch (e: InnerPrinterException) {
            // Já estava desligado.
        }
        servico = null
    }

    private fun estado(): Int = try {
        servico?.updatePrinterState() ?: SEM_IMPRESSORA
    } catch (e: Exception) {
        SEM_IMPRESSORA
    }

    val disponivel: Boolean
        get() = servico != null && estado() != SEM_IMPRESSORA

    /** 48 caracteres em papel de 80 mm; 32 em 58 mm. */
    val colunas: Int
        get() = try {
            if (servico?.printerPaper?.startsWith("80") == true) 48 else 32
        } catch (e: Exception) {
            32
        }

    /** Imprime as linhas já compostas pelo sistema. Devolve o erro em texto, ou vazio. */
    fun imprimir(linhasJson: String): String {
        val s = servico ?: return "A impressora deste PDA não está disponível."
        mensagemEstado(estado())?.let { return it }
        return try {
            val linhas = JSONArray(linhasJson)
            s.printerInit(null)
            for (i in 0 until linhas.length()) {
                val l = linhas.getJSONObject(i)
                s.setAlignment(l.getInt("a"), null)
                s.sendRAWData(byteArrayOf(0x1B, 0x45, if (l.getBoolean("n")) 1 else 0), null)
                s.setFontSize(if (l.getBoolean("g")) 32f else 24f, null)
                s.printText(l.getString("t") + "\n", null)
            }
            s.sendRAWData(byteArrayOf(0x1B, 0x45, 0), null)
            s.lineWrap(4, null)
            ""
        } catch (e: Exception) {
            "Não foi possível imprimir neste PDA: ${e.message}"
        }
    }

    private fun mensagemEstado(estado: Int): String? = when (estado) {
        1, 2 -> null
        3 -> "A impressora do PDA não responde."
        4 -> "A impressora do PDA não tem papel."
        5 -> "A impressora do PDA está demasiado quente. Aguarde um pouco."
        6 -> "A tampa da impressora do PDA está aberta."
        SEM_IMPRESSORA -> "Este PDA não tem impressora."
        else -> "A impressora do PDA tem um problema (código $estado)."
    }

    private companion object {
        const val SEM_IMPRESSORA = 505
    }
}
