// Facturação, cartões evento e contas correntes. (reexportados por api.ts)
import type { Pagina } from './comum'

// ---------- Facturação ----------

export type TipoDocumento = 'VendaDinheiro' | 'Factura' | 'Recibo' | 'Cotacao' | 'NotaCredito'
export type EstadoDocumento = 'Emitido' | 'Anulado' | 'Convertido'

export interface DocumentoResumo {
  id: number
  tipo: TipoDocumento
  numero: string
  estado: EstadoDocumento
  emitidoEmUtc: string
  clienteNome: string
  clienteNuit: string | null
  total: number
  emDivida: number
  vencimento: string | null
  validaAte: string | null
  origem: string | null
}

export interface DocumentoLinha {
  id: number
  produtoId: number | null
  codigo: string | null
  descricao: string
  quantidade: number
  precoUnitario: number
  desconto: number
  percentagemIva: number
  motivoIsencao: string | null
  base: number
  iva: number
  total: number
  /** Quantidade ainda por creditar (facturas e VD). */
  quantidadeCreditavel: number
}

export interface DocumentoRelacionado { id: number; tipo: TipoDocumento; numero: string; estado: EstadoDocumento; emitidoEmUtc: string; valor: number }

export interface DocumentoComercial {
  id: number
  tipo: TipoDocumento
  numero: string
  estado: EstadoDocumento
  emitidoEmUtc: string
  utilizador: string | null
  vencimento: string | null
  validaAte: string | null
  emitenteNome: string
  emitenteNuit: string | null
  clienteId: number | null
  clienteNome: string
  clienteNuit: string | null
  clienteEndereco: string | null
  pedidoId: number | null
  pedidoNumero: string | null
  documentoOrigemId: number | null
  documentoOrigemNumero: string | null
  documentoOrigemTipo: TipoDocumento | null
  subtotal: number
  desconto: number
  baseTributavel: number
  iva: number
  total: number
  valorPago: number
  valorCreditado: number
  emDivida: number
  metodoPagamento: string | null
  referencia: string | null
  motivo: string | null
  observacoes: string | null
  anuladoEmUtc: string | null
  anuladoPor: string | null
  motivoAnulacao: string | null
  linhas: DocumentoLinha[]
  resumoIva: { percentagem: number; base: number; iva: number; total: number }[]
  liquidacoes: { facturaId: number; facturaNumero: string; valor: number }[]
  relacionados: DocumentoRelacionado[]
  rowVersion: string
}

export interface FacturaEmDivida { id: number; numero: string; emitidoEmUtc: string; vencimento: string | null; total: number; valorPago: number; valorCreditado: number; emDivida: number; vencida: boolean }
export interface ContaCorrente { clienteId: number; clienteNome: string; clienteNuit: string | null; totalEmDivida: number; totalVencido: number; facturas: FacturaEmDivida[] }

// ---- Cartões Evento
export type TipoMovimentoCartao = 'Carregamento' | 'Recarga' | 'Pagamento' | 'AnulacaoPagamento' | 'Estorno' | 'Descarga' | 'TransferenciaSaida' | 'TransferenciaEntrada' | 'MudancaCliente'
export interface CartaoEvento { id: number; numero: string; titular: string | null; saldo: number; activo: boolean; criadoEmUtc: string; ultimoMovimentoUtc: string | null; rowVersion: string
  /** Cliente a quem o cartão pertence (adiantamentos). */
  clienteId?: number | null; clienteNome?: string | null }
export interface MovimentoCartao { id: number; dataUtc: string; tipo: TipoMovimentoCartao; valor: number; saldoApos: number; metodo: string | null; pedido: string | null; motivo: string | null; operador: string | null; sessaoCaixaId?: number | null }
export interface RelatorioCartoes {
  resumo: {
    carregado: number; carregamentos: number; recarregado: number; recargas: number; pago: number; pagamentos: number
    devolvido: number; devolucoes: number; cartoesNovos: number; saldoEmCirculacao: number; cartoesComSaldo: number; cartoesActivos: number
  }
  porMetodo: { metodo: string; valor: number; quantidade: number }[]
  porOperador: { operador: string; carregado: number; pago: number; movimentos: number }[]
  movimentos: { dataUtc: string; numero: string; titular: string | null; tipo: TipoMovimentoCartao; valor: number; saldoApos: number; metodo: string | null; pedido: string | null; operador: string | null; motivo: string | null }[]
  movimentosCortados: boolean
  saldos: { numero: string; titular: string | null; saldo: number; activo: boolean; ultimoMovimentoUtc: string | null }[]
}

// ---------- Contas correntes dos clientes ----------

/** Movimento do extrato: a factura debita; o recibo e a nota de crédito sobre factura creditam. */
export interface MovimentoContaCorrente { documentoId: number; tipo: TipoDocumento; numero: string; dataUtc: string; vencimento: string | null; descricao: string; debito: number; credito: number; saldo: number }
export interface ExtratoContaCorrente {
  clienteId: number; clienteNome: string; clienteNuit: string | null; clienteTelefone: string | null; clienteActivo: boolean
  desde: string; ate: string; saldoAnterior: number; debitos: number; creditos: number; saldoFinal: number
  movimentos: MovimentoContaCorrente[]; emDividaHoje: number; vencidoHoje: number; facturasEmAberto: FacturaEmDivida[]
}
export interface LinhaHistoricoCliente {
  id: number; tipo: TipoDocumento; numero: string; estado: EstadoDocumento; emitidoEmUtc: string; vencimento: string | null
  total: number; valorPago: number; valorCreditado: number; emDivida: number; origem: string | null; metodoPagamento: string | null
}
export interface TotaisHistoricoCliente { documentos: number; vendasDinheiro: number; facturado: number; recebido: number; creditado: number; emDivida: number }
export interface HistoricoCliente { clienteId: number; clienteNome: string; clienteNuit: string | null; desde: string; ate: string; totais: TotaisHistoricoCliente; documentos: LinhaHistoricoCliente[]; cortado: boolean }
export type SituacaoFactura = 'Paga' | 'Parcial' | 'EmDivida' | 'Vencida' | 'Creditada'
export interface LinhaFacturaCredito {
  id: number; numero: string; emitidoEmUtc: string; vencimento: string | null; clienteId: number | null; clienteNome: string; clienteNuit: string | null
  total: number; valorPago: number; valorCreditado: number; emDivida: number; situacao: SituacaoFactura; diasAtraso: number
}
export interface ResumoFacturasCredito { facturas: number; facturado: number; pago: number; creditado: number; emDivida: number; vencido: number }
export interface DividaCliente { clienteId: number; clienteNome: string; clienteNuit: string | null; facturas: number; emDivida: number; naoVencido: number; ate30: number; de31a60: number; de61a90: number; mais90: number }
export interface RelatorioFacturasCredito {
  desde: string; ate: string; hoje: string; resumo: ResumoFacturasCredito; facturas: LinhaFacturaCredito[]; cortado: boolean
  dividas: DividaCliente[]; totalEmDivida: number; totalVencido: number
}
export type CodigoEtiquetaCartao = 'CodigoBarras' | 'QrCode'

/** Extrato do cartão (página) com os totais do filtro; saldos de início e fim só no filtro por datas. */
export interface ExtratoCartao extends Pagina<MovimentoCartao> { entradas: number; saidas: number; saldoInicial: number | null; saldoFinal: number | null }
/** Caixa (sessão) com movimentos de cartões. */
export interface SessaoCartoes { id: number; terminal: string; abertaPor: string; abertaEmUtc: string; fechadaEmUtc: string | null; movimentos: number }

// ---------- Descargas de saldo dos Cartões Evento ----------

/** cartoesDeClientesFora/saldoDeClientesFora: cartões de clientes (adiantamentos) deixados de fora da descarga de todos. */
export interface SaldosDescarga {
  cartoes: { id: number; numero: string; titular: string | null; saldo: number; acumulado: number; activo: boolean; cliente?: string | null }[]; total: number
  cartoesDeClientesFora?: number; saldoDeClientesFora?: number
}

// ---------- Adiantamentos de clientes (cartões associados) ----------

export interface CartaoDoCliente { id: number; numero: string; saldo: number; activo: boolean; ultimoMovimentoUtc: string | null }
/** saldo: saldo da conta de adiantamentos depois do movimento (todos os cartões do cliente). */
export interface MovimentoAdiantamento { id: number; dataUtc: string; cartao: string; tipo: TipoMovimentoCartao; valor: number; saldo: number; metodo: string | null; pedido: string | null; motivo: string | null; operador: string | null }
export interface ExtratoAdiantamentos {
  clienteId: number; clienteNome: string; clienteNuit: string | null; clienteTelefone: string | null; clienteActivo: boolean
  desde: string; ate: string; saldoAnterior: number; recargas: number; consumos: number; outros: number; saldoFinal: number
  movimentos: MovimentoAdiantamento[]; cortado: boolean; saldoActual: number; cartoes: CartaoDoCliente[]
}
export interface LinhaAdiantamentoCliente {
  clienteId: number; cliente: string; telefone: string | null; cartoes: number; saldoInicial: number; recargas: number; consumos: number; outros: number; saldoFinal: number
  ultimaRecargaUtc: string | null; ultimoConsumoUtc: string | null
}
export interface RelatorioAdiantamentos {
  desde: string; ate: string; saldoInicial: number; recargas: number; consumos: number; outros: number; saldoFinal: number
  clientes: LinhaAdiantamentoCliente[]; recargasPorMetodo: { metodo: string; valor: number; quantidade: number }[]; recargasPorOperador: { operador: string; valor: number; quantidade: number }[]
}
export interface DescargaCartoes {
  id: number; dataUtc: string; operador: string | null; devolveuDinheiro: boolean; metodo: string | null; sessaoCaixaId: number | null; motivo: string; total: number
  cartoes: { numero: string; titular: string | null; saldo: number; acumulado: number }[]
}
export interface RelatorioDescargas {
  total: number; devolvido: number; retirado: number; cartoes: number; operacoes: number; cortado: boolean
  linhas: { descargaId: number; dataUtc: string; numero: string; titular: string | null; saldo: number; acumulado: number; devolveuDinheiro: boolean; metodo: string | null; operador: string | null; motivo: string | null; sessaoCaixaId: number | null }[]
}
