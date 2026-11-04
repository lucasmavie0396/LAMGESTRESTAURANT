import type { TipoDocumento } from '../../types/api'

export const NOMES_TIPO: Record<TipoDocumento, string> = {
  VendaDinheiro: 'Venda a Dinheiro',
  Factura: 'Factura',
  Recibo: 'Recibo',
  Cotacao: 'Cotação',
  NotaCredito: 'Nota de Crédito',
}
