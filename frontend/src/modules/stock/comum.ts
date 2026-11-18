import type { Artigo, TipoArtigo, TipoMovimentoStock } from '../../types/api'

export const n3 = new Intl.NumberFormat('pt-MZ', { maximumFractionDigits: 3 })

export const NOME_TIPO_ARTIGO: Record<TipoArtigo, string> = {
  Ingrediente: 'Ingrediente',
  Revenda: 'Revenda',
  Consumivel: 'Consumível',
}

export const NOME_MOVIMENTO: Record<TipoMovimentoStock, string> = {
  EntradaCompra: 'Entrada (compra)',
  EstornoCompra: 'Anulação de compra',
  SaidaVenda: 'Saída (venda)',
  EstornoVenda: 'Estorno de venda',
  Ajuste: 'Ajuste',
  Perda: 'Perda',
  TransferenciaSaida: 'Transferência (saída)',
  TransferenciaEntrada: 'Transferência (entrada)',
  Inventario: 'Inventário',
  StockInicial: 'Stock inicial',
}

/** Valor de um campo numérico de formulário (vazio → NaN para forçar validação). */
export function numero(texto: string) {
  return texto.trim() === '' ? NaN : Number(texto)
}

/** Artigos por categoria (ex.: «Carnes», «Legumes»), por ordem alfabética; os sem categoria no fim. */
export function agruparArtigos(artigos: Artigo[]): [string, Artigo[]][] {
  const grupos = new Map<string, Artigo[]>()
  for (const a of artigos) {
    const g = a.categoria ?? ''
    grupos.set(g, [...(grupos.get(g) ?? []), a])
  }
  return [...grupos.entries()]
    .sort(([a], [b]) => Number(a === '') - Number(b === '') || a.localeCompare(b, 'pt'))
    .map(([g, lista]) => [g || 'Sem categoria', lista.sort((x, y) => x.nome.localeCompare(y.nome, 'pt'))])
}
