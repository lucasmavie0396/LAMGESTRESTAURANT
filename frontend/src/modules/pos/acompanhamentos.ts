import type { GrupoPos } from '../../types/api'

/**
 * Toque numa opção: nos grupos de uma só escolha troca a escolhida; nos restantes marca/desmarca,
 * sem passar do máximo do grupo.
 */
export function alternarOpcao(escolhidas: number[], grupo: GrupoPos, opcaoId: number): number[] {
  if (escolhidas.includes(opcaoId)) return escolhidas.filter((id) => id !== opcaoId)
  const doGrupo = grupo.opcoes.map((o) => o.id)
  if (grupo.maximo === 1) return [...escolhidas.filter((id) => !doGrupo.includes(id)), opcaoId]
  if (escolhidas.filter((id) => doGrupo.includes(id)).length >= grupo.maximo) return escolhidas
  return [...escolhidas, opcaoId]
}

/** Grupos que ainda não têm o mínimo de escolhas. */
export function gruposEmFalta(grupos: GrupoPos[], escolhidas: number[]): GrupoPos[] {
  return grupos.filter((g) => g.opcoes.filter((o) => escolhidas.includes(o.id)).length < g.minimo)
}
