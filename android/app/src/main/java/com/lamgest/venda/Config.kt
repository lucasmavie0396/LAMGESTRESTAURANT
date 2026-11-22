package com.lamgest.venda

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/**
 * Lojas guardadas neste dispositivo (cada loja tem o seu servidor) e a loja actual.
 * [servidor], [servidorExterno], [terminalId] e [posto] referem-se sempre à loja actual.
 * Um tablet de garçom tem só a sua loja; um aparelho de gestor ([gestor]) recebe também as outras lojas ligadas.
 */
class Config(contexto: Context) {
    private val prefs = contexto.getSharedPreferences("lamgest", Context.MODE_PRIVATE)

    /** Uma loja: [id] é o identificador do servidor (ou o endereço, se ainda não se conhece). */
    data class Loja(
        val id: String,
        val nome: String,
        val servidor: String,
        val servidorExterno: String,
        val terminalId: Int,
        val posto: String,
        /** Autoridade local do servidor (Base64, DER) para o HTTPS; vazio = loja em HTTP. */
        val certificado: String = "",
    ) {
        fun json(): JSONObject = JSONObject().put("id", id).put("nome", nome).put("servidor", servidor)
            .put("servidorExterno", servidorExterno).put("terminalId", terminalId).put("posto", posto).put("certificado", certificado)

        companion object {
            fun de(o: JSONObject) = Loja(o.optString("id"), o.optString("nome"), o.optString("servidor"), o.optString("servidorExterno"),
                o.optInt("terminalId"), o.optString("posto"), o.optString("certificado"))
        }
    }

    init {
        // Versões anteriores guardavam uma só ligação: passa a ser a primeira loja.
        if (!prefs.contains("lojas") && (prefs.getString("servidor", "") ?: "").isNotEmpty()) {
            val antiga = Loja(prefs.getString("servidor", "")!!, prefs.getString("posto", "") ?: "", prefs.getString("servidor", "")!!,
                prefs.getString("servidorExterno", "") ?: "", prefs.getInt("terminalId", 0), prefs.getString("posto", "") ?: "")
            gravar(listOf(antiga), antiga.id)
        }
    }

    val lojas: List<Loja>
        get() = runCatching {
            val a = JSONArray(prefs.getString("lojas", "[]"))
            (0 until a.length()).map { Loja.de(a.getJSONObject(it)) }
        }.getOrDefault(emptyList())

    val lojaActual: Loja?
        get() = lojas.let { l -> l.firstOrNull { it.id == prefs.getString("lojaActual", "") } ?: l.firstOrNull() }

    private fun gravar(lista: List<Loja>, actual: String?) {
        prefs.edit().putString("lojas", JSONArray(lista.map { it.json() }).toString()).apply {
            if (actual != null) putString("lojaActual", actual)
        }.apply()
    }

    /** Acrescenta ou substitui (mesmo [Loja.id] ou mesmo servidor) e, se pedido, torna-a a actual. */
    fun guardarLoja(loja: Loja, tornarActual: Boolean) {
        val outras = lojas.filterNot { it.id == loja.id || (loja.servidor.isNotEmpty() && it.servidor == loja.servidor) }
        gravar(outras + loja, if (tornarActual) loja.id else null)
    }

    fun mudarPara(id: String): Boolean {
        if (lojas.none { it.id == id }) return false
        prefs.edit().putString("lojaActual", id).apply()
        return true
    }

    private fun alterarActual(f: (Loja) -> Loja) {
        val actual = lojaActual ?: return
        guardarLoja(f(actual), tornarActual = true)
    }

    val servidor: String get() = lojaActual?.servidor ?: ""

    /** Endereço da loja actual fora do restaurante (VPN), recebido do servidor. Vazio = só rede local. */
    var servidorExterno: String
        get() = lojaActual?.servidorExterno ?: ""
        set(valor) = alterarActual { it.copy(servidorExterno = valor) }

    /** Autoridade local da loja actual (vazio = sem HTTPS). */
    val certificado: String get() = lojaActual?.certificado ?: ""

    val terminalId: Int get() = lojaActual?.terminalId ?: 0
    val posto: String get() = lojaActual?.posto ?: ""

    /** Aparelho de gestor: recebe as outras lojas ligadas e mostra o seletor de lojas. */
    var gestor: Boolean
        get() = prefs.getBoolean("gestor", false)
        set(valor) = prefs.edit().putBoolean("gestor", valor).apply()

    /** "posto" ou "pda". */
    var destino: String
        get() = prefs.getString("destino", "posto") ?: "posto"
        set(valor) = prefs.edit().putString("destino", valor).apply()

    /** Há uma loja para abrir: emparelhada (com posto) ou recebida de outra loja (gestor, consulta pela VPN). */
    val emparelhado: Boolean
        get() = lojaActual?.let { it.terminalId > 0 || it.servidorExterno.isNotEmpty() } ?: false
}
