package com.lamgest.venda

import android.content.Context

/** Ligação guardada neste dispositivo: servidor, posto emparelhado e onde saem os recibos. */
class Config(contexto: Context) {
    private val prefs = contexto.getSharedPreferences("lamgest", Context.MODE_PRIVATE)

    var servidor: String
        get() = prefs.getString("servidor", "") ?: ""
        set(valor) = prefs.edit().putString("servidor", valor).apply()

    var terminalId: Int
        get() = prefs.getInt("terminalId", 0)
        set(valor) = prefs.edit().putInt("terminalId", valor).apply()

    var posto: String
        get() = prefs.getString("posto", "") ?: ""
        set(valor) = prefs.edit().putString("posto", valor).apply()

    /** "posto" ou "pda". */
    var destino: String
        get() = prefs.getString("destino", "posto") ?: "posto"
        set(valor) = prefs.edit().putString("destino", valor).apply()

    val emparelhado: Boolean
        get() = servidor.isNotEmpty() && terminalId > 0
}
