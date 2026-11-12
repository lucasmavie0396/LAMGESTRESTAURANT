/**
 * Mesa aberta por outro utilizador que o utilizador actual não pode ver nem lançar (sem a permissão pos.mesas-outros).
 * O servidor recusa sempre; aqui serve para mostrar o aviso antes de tentar.
 */
export function mesaDeOutro(mesa: { responsavelId?: number | null }, utilizadorId: number | undefined, veMesasDeOutros: boolean) {
  return !!mesa.responsavelId && mesa.responsavelId !== utilizadorId && !veMesasDeOutros
}

export const avisoMesaDeOutro = (mesa: { nome: string; responsavel?: string | null }) =>
  `A mesa ${mesa.nome} foi aberta por ${mesa.responsavel ?? 'outro utilizador'}. Não tem permissão para ver nem lançar em mesas de outros utilizadores.`
