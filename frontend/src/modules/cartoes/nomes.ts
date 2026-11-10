import type { TipoMovimentoCartao } from '../../types/api'

export const NOME_MOVIMENTO_CARTAO: Record<TipoMovimentoCartao, string> = {
  Carregamento: 'Carregamento',
  Recarga: 'Recarga',
  Pagamento: 'Pagamento',
  AnulacaoPagamento: 'Pagamento anulado',
  Estorno: 'Estorno',
}
