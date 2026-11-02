import type { TipoArtigo, TipoMovimentoStock } from '../../types/api'

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
}

/** Valor de um campo numérico de formulário (vazio → NaN para forçar validação). */
export function numero(texto: string) {
  return texto.trim() === '' ? NaN : Number(texto)
}
