import { api } from '../../api/cliente'
import { mensagemErro } from '../../hooks/useDados'
import type { ConfiguracoesPublicas, DocumentoImpressao } from '../../types/api'
import { imprimirNoNavegador } from '../../utils/impressaoDocumento'
import { imprimeNoPda, imprimirNoPda } from '../../utils/pda'
import { lerTerminalLocal } from '../../utils/terminalLocal'

/** Recibo de 80 mm do carregamento ou recarga: na impressora de pagamentos do terminal ou, sem ela, no PDA ou na janela de impressão do posto. */
export async function imprimirReciboCartao(id: number, aoFalhar: (texto: string) => void) {
  try {
    const noPda = imprimeNoPda()
    const r = await api.post<{ impressora: string | null; documento: DocumentoImpressao }>(`/api/cartoes/${id}/recibo`, { terminalId: lerTerminalLocal(), semImpressora: noPda })
    if (noPda) imprimirNoPda(r.documento)
    else if (!r.impressora) imprimirNoNavegador(r.documento, 80, await api.get<ConfiguracoesPublicas>('/api/configuracoes/publicas'), 'recibo')
  } catch (e) {
    aoFalhar(mensagemErro(e, 'O cartão ficou registado, mas não foi possível imprimir o recibo.'))
  }
}
