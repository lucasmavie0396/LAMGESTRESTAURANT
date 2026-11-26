package com.lamgest.venda

import android.webkit.JavascriptInterface
import org.json.JSONArray
import org.json.JSONObject

/** Ponte `window.LamgestPDA` usada pelo sistema (frontend/src/utils/pda.ts). */
class PontePda(
    private val config: Config,
    private val impressora: ImpressoraSunmi,
    private val abrirConfiguracao: () -> Unit,
    private val lerComCamara: () -> Unit,
    private val mudarDeLoja: (String) -> Unit,
    private val nfc: LeitorNfc,
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

    /** Abre a câmara para ler um QR code ou código de barras; o resultado chega à página no evento `lamgest-codigo`. */
    @JavascriptInterface fun lerCodigo() = lerComCamara()

    /** O telemóvel tem leitor NFC (cartões sem contacto)? */
    @JavascriptInterface fun temNfc(): Boolean = nfc.existe

    /** Espera um cartão sem contacto; o número do chip chega no evento `lamgest-nfc`. Devolve "", "sem-nfc" ou "desligado". */
    @JavascriptInterface fun lerNfc(): String = nfc.comecar()

    @JavascriptInterface fun pararNfc() = nfc.parar()

    /** Abre as definições do Android para ligar o NFC. */
    @JavascriptInterface fun definicoesNfc() = nfc.abrirDefinicoes()

    /** Lojas guardadas neste aparelho, para o seletor do ecrã de entrada: [{ id, nome, actual }]. */
    @JavascriptInterface fun lojas(): String {
        val actual = config.lojaActual?.id
        return JSONArray(config.lojas.map { JSONObject().put("id", it.id).put("nome", it.nome).put("actual", it.id == actual) }).toString()
    }

    /** Muda para outra loja guardada: a app volta a ligar-se (rede da loja ou VPN). */
    @JavascriptInterface fun mudarLoja(id: String) {
        if (config.mudarPara(id)) mudarDeLoja(id)
    }
}
