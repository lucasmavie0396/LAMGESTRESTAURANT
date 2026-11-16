import { api } from '../api/cliente'
import type { ListaLojas } from '../types/api'
import { pda } from './pda'

/** Loja no seletor: `id` é o da lista do servidor ou, na app Android, o identificador guardado no aparelho. */
export interface OpcaoLoja { id: string; nome: string; cor: string | null; actual: boolean; endereco?: string }

/**
 * Lojas para o seletor. Na app Android vêm do aparelho (só as lojas que ele conhece: um tablet de garçom só tem a sua);
 * no computador, da lista do servidor (Configurações → Lojas). Lista vazia ou só com esta loja = sem seletor.
 */
export async function lojasDisponiveis(): Promise<OpcaoLoja[]> {
  const ponte = pda()
  if (ponte?.lojas) {
    try {
      return (JSON.parse(ponte.lojas()) as { id: string; nome: string; actual: boolean }[]).map((l) => ({ ...l, cor: null }))
    } catch {
      return []
    }
  }
  const lista = await api.get<ListaLojas>('/api/lojas')
  return [
    { id: 'actual', nome: lista.actual.nome, cor: lista.actual.cor, actual: true },
    ...lista.outras.map((l) => ({ id: String(l.id), nome: l.nome, cor: l.cor, actual: false, endereco: l.endereco })),
  ]
}

/** Vai para a entrada de outra loja: na app Android a própria app liga-se ao servidor dela; no computador, o navegador muda de endereço. */
export function irParaLoja(loja: OpcaoLoja) {
  const ponte = pda()
  if (ponte?.mudarLoja) return ponte.mudarLoja(loja.id)
  if (loja.endereco) window.location.href = `${loja.endereco}/login`
}
