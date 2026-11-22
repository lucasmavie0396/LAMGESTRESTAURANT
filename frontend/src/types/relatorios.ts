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
