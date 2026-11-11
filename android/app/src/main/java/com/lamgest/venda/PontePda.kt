package com.lamgest.venda

import android.webkit.JavascriptInterface

/** Ponte `window.LamgestPDA` usada pelo sistema (frontend/src/utils/pda.ts). */
class PontePda(
    private val config: Config,
    private val impressora: ImpressoraSunmi,
    private val abrirConfiguracao: () -> Unit,
) {
    @JavascriptInterface fun terminalId(): Int = config.terminalId
    @JavascriptInterface fun posto(): String = config.posto
    @JavascriptInterface fun destino(): String = config.destino
    @JavascriptInterface fun definirDestino(destino: String) {
        config.destino = if (destino == "pda") "pda" else "posto"
    }
    @JavascriptInterface fun temImpressora(): Boolean = impressora.disponivel
    @JavascriptInterface fun colunas(): Int = impressora.colunas
    @JavascriptInterface fun imprimir(linhasJson: String): String = impressora.imprimir(linhasJson)
    @JavascriptInterface fun configurar() = abrirConfiguracao()
}
