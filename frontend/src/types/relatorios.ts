// Relatórios. (reexportados por api.ts)
import type { EstadoSessaoCaixa } from './caixa'
import type { Pagina } from './comum'
import type { TipoMovimentoStock } from './stock'
import type { EstadoPedido, TipoPedido } from './vendas'

// ---------- Relatórios ----------

export interface LinhaValor { rotulo: string; valor: number; quantidade: number }
export interface VendaDia { dia: string; total: number; pedidos: number }
export interface VendaProduto { produtoId: number; produto: string; categoria: string; quantidade: number; total: number; custoEstimado: number; codigo: string; unidade?: string }
export interface ProdutoSemVenda { produtoId: number; codigo: string; produto: string; categoria: string }
/** Quem registou o pedido: vendas, facturação, descontos concedidos e métodos com que foram pagas. */
export interface VendaOperador { operador: string; vendas: number; total: number; descontos: number; metodos: LinhaValor[] }
export interface VendaCategoria { categoria: string; quantidade: number; total: number }
export interface ResumoCancelamentos {
  pedidosCancelados: number
  valorPedidosCancelados: number
  itensCancelados: number
  valorItensCancelados: number
  pedidosEstornados: number
  valorEstornado: number
}

export interface RelatorioVendas {
  desde: string
  ate: string
  totalVendas: number
  numeroPedidos: number
  ticketMedio: number
  descontos: number
  impostos: number
  taxasEntrega: number
  receitaLiquida: number
  custoEstimado: number
  margemEstimada: number
  margemPercentagem: number | null
  metodologiaMargem: string
  porDia: VendaDia[]
  porMetodo: LinhaValor[]
  porTipo: LinhaValor[]
  porCategoria: VendaCategoria[]
  produtos: VendaProduto[]
  porUtilizador: LinhaValor[]
  porGarcom: LinhaValor[]
  cancelamentos: ResumoCancelamentos
  produtosSemVendas: ProdutoSemVenda[] | null
  operadores: VendaOperador[] | null
}

/** Estado do pedido nos relatórios: Pago (vendas), Cancelado ou Estornado. */
export interface LinhaVendaRelatorio {
  id: number
  numero: string
  dataUtc: string
  tipo: TipoPedido
  mesa: string | null
  cliente: string | null
  operador: string | null
  garcom: string | null
  subtotal: number
  desconto: number
  impostos: number
  taxaEntrega: number
  total: number
  recebido: number
  troco: number
  metodos: string
  estado: EstadoPedido
  recibo: string | null
  motivo: string | null
}
export interface TotaisListaVendas { vendas: number; subtotal: number; desconto: number; impostos: number; taxaEntrega: number; total: number; recebido: number; troco: number }
export interface OpcaoFiltro { id: number; nome: string }
export interface ListaVendas { vendas: Pagina<LinhaVendaRelatorio>; totais: TotaisListaVendas; operadores: OpcaoFiltro[]; metodos: OpcaoFiltro[] }

export interface VendaCliente { clienteId: number; nome: string; telefone: string | null; cartao: string | null; compras: number; total: number; descontos: number; ultimaCompraUtc: string }
export interface RelatorioClientes {
  desde: string
  ate: string
  clientes: VendaCliente[]
  vendasComCliente: number
  totalComCliente: number
  vendasSemCliente: number
  totalSemCliente: number
}

export interface ImpostoTaxa { percentagem: number; taxa: string; baseSemImposto: number; imposto: number; comImposto: number }
export interface ImpostoPeriodo { periodo: string; vendas: number; descontos: number; baseSemImposto: number; imposto: number; taxasEntrega: number; total: number }
export interface RelatorioImpostos {
  desde: string
  ate: string
  agrupamento: 'dia' | 'mes'
  vendas: number
  subtotal: number
  descontos: number
  baseSemImposto: number
  impostos: number
  taxasEntrega: number
  total: number
  porTaxa: ImpostoTaxa[]
  porPeriodo: ImpostoPeriodo[]
}

export interface PedidoAnulado { id: number; numero: string; dataUtc: string; tipo: TipoPedido; mesa: string | null; por: string | null; motivo: string | null; valor: number; recibo: string | null }
export interface ItemAnulado { pedidoId: number; pedido: string; dataUtc: string; produto: string; quantidade: number; valor: number; por: string | null; motivo: string | null }
export interface PagamentoAnulado { pedidoId: number; pedido: string; dataUtc: string; metodo: string; valor: number; por: string | null; motivo: string | null }
export interface RelatorioAnulacoes {
  desde: string
  ate: string
  pedidosCancelados: PedidoAnulado[]
  estornos: PedidoAnulado[]
  itensCancelados: ItemAnulado[]
  pagamentosAnulados: PagamentoAnulado[]
}

export interface FechoSessao {
  sessaoId: number
  terminal: string
  abertaPor: string
  fechadaPor: string | null
  abertaEmUtc: string
  fechadaEmUtc: string
  esperado: number
  contado: number
  diferenca: number
  observacoes: string | null
}
export interface DiferencaMetodo { metodo: string; esperado: number; contado: number; diferenca: number }
export interface RelatorioFinanceiro {
  desde: string
  ate: string
  totalVendas: number
  numeroPedidos: number
  impostos: number
  vendasSemImpostos: number
  custoEstimadoVendas: number
  despesas: number
  resultadoEstimado: number
  estornos: number
  pedidosEstornados: number
  comprasConfirmadas: number
  numeroCompras: number
  recebimentosPorMetodo: LinhaValor[]
  despesasPorCategoria: LinhaValor[]
  despesasPorMetodo: LinhaValor[]
  sessoesFechadas: FechoSessao[]
  diferencasPorMetodo: DiferencaMetodo[]
  sessoesAbertas: number
  metodologia: string
}

export interface ArtigoValor { artigoId: number; codigo: string; artigo: string; unidade: string; quantidade: number; valor: number; armazem?: string | null }
export interface RelatorioStock {
  desde: string
  ate: string
  valorStockActual: number
  artigosActivos: number
  artigosAbaixoMinimo: number
  valorPorTipo: LinhaValor[]
  valorPorArmazem: LinhaValor[]
  movimentosPorTipo: { tipo: TipoMovimentoStock; movimentos: number; valor: number }[]
  consumo: ArtigoValor[]
  ajustesPerdas: ArtigoValor[]
  entradas: ArtigoValor[]
  comprasPorFornecedor: LinhaValor[]
  metodologia: string
}

export interface Dashboard {
  dia: string
  vendasHoje: number
  pedidosHoje: number
  ticketMedioHoje: number
  vendasOntem: number
  ultimos7Dias: VendaDia[]
  porMetodoHoje: LinhaValor[]
  topProdutosHoje: VendaProduto[]
  pedidosAbertos: number
  valorEmAberto: number
  itensCozinhaPendentes: number
  itensCozinhaEmPreparacao: number
  envioMaisAntigoPendenteUtc: string | null
  caixas: { sessaoId: number; terminal: string; operador: string; abertaEmUtc: string }[] | null
  stockBaixo: { artigoId: number; artigo: string; saldo: number; minimo: number; unidade: string }[] | null
  cancelamentosHoje: ResumoCancelamentos
  vendasMes: number
  pedidosMes: number
  /** Mês anterior, do dia 1 ao mesmo dia do mês. */
  vendasMesAnterior: number
}

export interface SessaoCaixaResumo {
  id: number
  terminal: string
  estado: EstadoSessaoCaixa
  abertaPor: string
  abertaEmUtc: string
  fechadaEmUtc: string | null
  fundoInicial: number
  totalDiferenca: number | null
}

// ---------- Relatórios de operação ----------

export interface LinhaValorQuantidade { nome: string; quantidade: number; valor: number }
export interface RelatorioContasPagar {
  desde: string; ate: string; hoje: string; totalEmDivida: number; totalVencido: number
  dividas: { fornecedorId: number; fornecedor: string; compras: number; emDivida: number; naoVencido: number; ate30: number; de31a60: number; de61a90: number; mais90: number }[]
  comprasEmAberto: { id: number; fornecedor: string; documento: string; data: string; vencimento: string | null; total: number; pago: number; emDivida: number; diasAtraso: number }[]
  pagoNoPeriodo: number
  pagamentos: { dataUtc: string; fornecedor: string; documento: string; metodo: string; referencia: string | null; valor: number; anulado: boolean }[]
  porMetodo: LinhaValorQuantidade[]
}
export interface RelatorioHoras {
  desde: string; ate: string; pedidos: number; valor: number
  porHora: { hora: number; pedidos: number; valor: number; valorMedio: number }[]
  porDiaSemana: { diaSemana: number; nome: string; pedidos: number; valor: number; dias: number; mediaPorDia: number }[]
  celulas: { diaSemana: number; hora: number; pedidos: number; valor: number }[]
  horaPico: number | null; diaPico: string | null
}
export type SituacaoCotacao = 'Aberta' | 'Expirada' | 'Convertida' | 'Anulada'
export interface RelatorioCotacoes {
  desde: string; ate: string; emitidas: number; valorEmitido: number; convertidas: number; valorConvertido: number; abertas: number; valorAberto: number
  expiradas: number; valorExpirado: number; anuladas: number; taxaConversao: number; diasMedioConversao: number | null; cortado: boolean
  linhas: { id: number; numero: string; emitidoEmUtc: string; validaAte: string | null; cliente: string; total: number; situacao: SituacaoCotacao; factura: string | null; diasAteConversao: number | null }[]
}
export interface LinhaTempo { nome: string; estacao: string | null; itens: number; mediaMinutos: number; maximoMinutos: number; atrasados: number }
export interface RelatorioCozinha {
  desde: string; ate: string; limiteMinutos: number; itens: number; mediaMinutos: number; medianaMinutos: number; atrasados: number; porTerminar: number
  porEstacao: LinhaTempo[]; porPrato: LinhaTempo[]
  maisLentos: { enviadoEmUtc: string; pedido: string; mesa: string | null; produto: string; estacao: string | null; minutos: number }[]
}
export interface RelatorioDelivery {
  desde: string; ate: string; pedidos: number; valor: number; taxaEntrega: number; cancelados: number; mediaMinutosEntrega: number | null
  porTipo: { tipo: 'TakeAway' | 'Delivery'; pedidos: number; pagos: number; valor: number; cancelados: number; mediaMinutos: number | null }[]
  porEstafeta: { estafeta: string; entregas: number; valor: number; taxaEntrega: number; mediaMinutos: number | null }[]
}
export interface LinhaMesaRelatorio { mesa: string; zona: string; pedidos: number; pessoas: number; valor: number; mediaMinutos: number; valorPorPessoa: number | null }
export interface RelatorioMesas {
  desde: string; ate: string; pedidos: number; pessoas: number; valor: number; mediaMinutos: number; valorPorPessoa: number | null
  porZona: LinhaMesaRelatorio[]; porMesa: LinhaMesaRelatorio[]
}
export interface RelatorioPontos {
  desde: string; ate: string; ganhos: number; usados: number; ajustes: number; estornos: number; saldoTotal: number; clientesComSaldo: number
  clientes: { clienteId: number; cliente: string; telefone: string | null; ganhos: number; usados: number; ajustes: number; estornos: number; saldo: number }[]
}
export type SituacaoReserva = 'Compareceu' | 'Faltou' | 'Cancelada' | 'PorVir'
export interface RelatorioReservas {
  desde: string; ate: string; reservas: number; pessoas: number; compareceram: number; faltaram: number; canceladas: number; porVir: number
  taxaComparencia: number | null; porDiaSemana: LinhaValorQuantidade[]; cortado: boolean
  linhas: { inicioUtc: string; cliente: string; telefone: string | null; pessoas: number; mesa: string | null; situacao: SituacaoReserva; motivo: string | null }[]
}
