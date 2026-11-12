import { api } from '../api/cliente'
import { mensagemErro } from '../hooks/useDados'
import type { ConfiguracoesPublicas, DocumentoImpressao } from '../types/api'
import { imprimirNoNavegador, type LarguraPapel } from './impressaoDocumento'
import { lerTerminalLocal } from './terminalLocal'

interface FechoAutomatico { automatico: boolean; impressora: string | null; pdf: boolean; documento: DocumentoImpressao | null }

/**
 * Imprime o fecho logo depois de confirmado (Configurações → Documentos e POS → «Imprimir o fecho automaticamente»):
 * na impressora de caixa do terminal ou, sem ela no servidor, pela janela de impressão deste computador.
 * Uma falha de impressão nunca desfaz o fecho: só avisa (o botão «Imprimir» continua na janela do relatório).
 */
export async function imprimirFechoAutomatico(
  alvo: { sessaoId: number } | { diaId: number },
  avisos: { sucesso: (t: string) => void; erro: (t: string) => void },
  papelNavegador: LarguraPapel,
) {
  const nome = 'sessaoId' in alvo ? 'Fecho de caixa' : 'Fecho do dia'
  try {
    const r = await api.post<FechoAutomatico>('/api/relatorios/fecho/imprimir-automatico', {
      sessaoId: 'sessaoId' in alvo ? alvo.sessaoId : null,
      diaId: 'diaId' in alvo ? alvo.diaId : null,
      terminalId: lerTerminalLocal(),
    })
    if (!r.automatico) return
    if (r.impressora) {
      avisos.sucesso(r.pdf ? `${nome}: o PDF abre dentro de momentos.` : `${nome} enviado para a impressora ${r.impressora}.`)
    } else if (r.documento) {
      const publicas = await api.get<ConfiguracoesPublicas>('/api/configuracoes/publicas').catch(() => null)
      imprimirNoNavegador(r.documento, papelNavegador, publicas, 'fecho')
    }
  } catch (e) {
    avisos.erro(`${nome} fechado, mas não foi possível imprimir: ${mensagemErro(e)} Use o botão «Imprimir».`)
  }
}
