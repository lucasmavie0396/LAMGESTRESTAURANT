package com.lamgest.venda

import android.app.Activity
import android.content.Intent
import android.nfc.NfcAdapter
import android.nfc.Tag
import android.provider.Settings
import java.util.Locale

/**
 * Lê o número único (UID) do chip dos cartões sem contacto (NFC: MIFARE, NTAG, ...) com o leitor do próprio telemóvel.
 * Só lê o número do chip, nunca o conteúdo. Cartões só com banda magnética e cartões de proximidade de 125 kHz não têm
 * chip que o telemóvel leia.
 *
 * Pode ser chamado de qualquer thread (a ponte JavaScript corre fora da thread da interface).
 */
class LeitorNfc(private val actividade: Activity, private val aoLer: (String) -> Unit) {
    private val adaptador: NfcAdapter? = NfcAdapter.getDefaultAdapter(actividade)

    // Só mexido na thread da interface.
    private var aLer = false
    private var visivel = false

    val existe: Boolean get() = adaptador != null

    /** "" se ficou à espera do cartão; "sem-nfc" se o telemóvel não tem NFC; "desligado" se o NFC está desligado. */
    fun comecar(): String {
        val a = adaptador ?: return "sem-nfc"
        if (!a.isEnabled) return "desligado"
        actividade.runOnUiThread {
            aLer = true
            if (visivel) activar()
        }
        return ""
    }

    fun parar() = actividade.runOnUiThread {
        aLer = false
        desactivar()
    }

    fun abrirDefinicoes() = actividade.runOnUiThread {
        runCatching { actividade.startActivity(Intent(Settings.ACTION_NFC_SETTINGS)) }
            .onFailure { runCatching { actividade.startActivity(Intent(Settings.ACTION_WIRELESS_SETTINGS)) } }
    }

    /** onResume da actividade: o modo de leitura só funciona com a app à frente. */
    fun aoRetomar() {
        visivel = true
        if (aLer) activar()
    }

    /** onPause da actividade. */
    fun aoPausar() {
        visivel = false
        desactivar()
    }

    private fun activar() {
        val a = adaptador ?: return
        runCatching {
            // Modo de leitura: o cartão vem para a app (e não abre o «Nova etiqueta» do Android).
            a.enableReaderMode(actividade, { tag -> lido(tag) }, FLAGS, null)
        }
    }

    private fun desactivar() {
        runCatching { adaptador?.disableReaderMode(actividade) }
    }

    private fun lido(tag: Tag) {
        val uid = tag.id ?: return
        if (uid.isEmpty()) return
        actividade.runOnUiThread {
            if (!aLer) return@runOnUiThread
            aLer = false
            desactivar()
            aoLer(hex(uid))
        }
    }

    companion object {
        private const val FLAGS = NfcAdapter.FLAG_READER_NFC_A or NfcAdapter.FLAG_READER_NFC_B or NfcAdapter.FLAG_READER_NFC_F or
            NfcAdapter.FLAG_READER_NFC_V or NfcAdapter.FLAG_READER_SKIP_NDEF_CHECK

        /** UID em hexadecimal, maiúsculas, pela ordem do chip (ex.: 04A1B2C3D4E580). */
        fun hex(bytes: ByteArray): String = bytes.joinToString("") { String.format(Locale.ROOT, "%02X", it.toInt() and 0xFF) }
    }
}
