// Clientes, reservas, despesas, funcionários e configurações. (reexportados por api.ts)
import type { FuncaoImpressora, ModoImpressora } from './caixa'
import type { LinhaValor } from './relatorios'
import type { EstadoPedido, TipoPedido } from './vendas'

// ---------- Clientes, reservas, despesas, funcionários, configurações ----------

export type TipoCliente = 'Particular' | 'Empresa'
export interface Cliente {
  id: number
  tipo: TipoCliente
  nome: string
  telefone: string | null
  email: string | null
  endereco: string | null
  nuit: string | null
  observacoes: string | null
  cartaoFidelidade: string | null
  saldoPontos: number
  activo: boolean
  numeroPedidos: number
  totalGasto: number
  ultimaVisitaUtc: string | null
  rowVersion: string
}
export interface PedidoCliente { id: number; numero: string; tipo: TipoPedido; estado: EstadoPedido; total: number; criadoEmUtc: string }
export interface MovimentoPontos { id: number; tipo: 'Acumulacao' | 'Resgate' | 'Ajuste' | 'Estorno'; pontos: number; motivo: string | null; pedido: string | null; dataUtc: string }

export type EstadoReserva = 'Pendente' | 'Confirmada' | 'Chegada' | 'Concluida' | 'Cancelada'
export interface Reserva {
  id: number
  clienteId: number | null
  nomeCliente: string
  telefone: string | null
  inicioUtc: string
  duracaoMinutos: number
  numeroPessoas: number
  mesaId: number | null
  mesaNome: string | null
  estado: EstadoReserva
  observacoes: string | null
  motivoCancelamento: string | null
  rowVersion: string
}

export interface CategoriaDespesa { id: number; nome: string; activo: boolean }
export interface Despesa {
  id: number
  categoriaDespesaId: number
  categoria: string
  dataUtc: string
  valor: number
  descricao: string
  metodoPagamento: string
  referencia: string | null
  sessaoCaixaId: number | null
  estado: 'Confirmada' | 'Anulada'
  utilizador: string | null
  anuladaEmUtc: string | null
  motivoAnulacao: string | null
  rowVersion: string
}
export interface ResumoDespesas { desde: string; ate: string; total: number; numero: number; porCategoria: LinhaValor[] }

export type FuncaoFuncionario = 'Garcom' | 'Cozinheiro' | 'Caixa' | 'Estafeta' | 'Barman' | 'Gerente' | 'Outro'
export interface Funcionario {
  id: number
  nome: string
  funcao: FuncaoFuncionario
  telefone: string | null
  email: string | null
  documentoIdentificacao: string | null
  dataAdmissao: string | null
  utilizadorId: number | null
  utilizador: string | null
  activo: boolean
}
export interface Opcao { id: number; nome: string }

export interface Configuracao { chave: string; valor: string; descricao: string | null }
export interface LinhaImportacaoProduto {
  linha: number
  codigo: string
  nome: string
  categoria: string
  subcategoria: string | null
  precoVenda: number | null
  stockInicial: number | null
  erros: string[]
}
export interface AnaliseImportacaoProdutos { total: number; comErros: number; categoriasNovas: string[]; linhas: LinhaImportacaoProduto[] }
export interface ResultadoImportacaoProdutos { importados: number; categoriasCriadas: string[] }

/** `mostrarStock`: stock nos botões dos produtos. `sugestoes`: comentários rápidos dos produtos. */
export interface OpcoesPos { mesa: boolean; takeAway: boolean; delivery: boolean; balcao: boolean; codigoBarras: boolean; mostrarStock?: boolean; sugestoes?: string[] }
/** Stock de um produto no POS (unidades vendidas); `baixo`: no mínimo ou abaixo. */
export interface StockPos { produtoId: number; quantidade: number; baixo: boolean }
export interface ConfiguracoesPublicas {
  /** Hora (0 a 8) a que termina o dia comercial; 0 = meia-noite. */
  fimDiaComercial?: number
  /** Tabela de preço do botão «Retalho» no balcão (null = sem botão). */
  tabelaRetalhoId?: number | null
  nomeEstabelecimento: string
  moeda: string
  simboloMoeda: string
  fusoHorario: string
  logotipo: string | null
  pos: OpcoesPos | null
  logotipo80mm: boolean
  /** Cópias por documento (conta, venda-dinheiro, fecho…). */
  copias: Record<string, number> | null
  /** Endereço do servidor para os tablets fora do restaurante (VPN); a app guarda-o e usa-o com dados móveis. */
  enderecoExterno?: string | null
}

/** Documento imprimível (recibos e relatórios), igual ao que o servidor envia às impressoras. */
/** Linha de documento impresso. Com `celulas`: linha de tabela (1.ª célula = descrição, restantes = valores); `cabecalho` = títulos das colunas. */
export interface LinhaImpressao { texto: string; direita: string | null; alinhamento: 'Esquerda' | 'Centro' | 'Direita'; negrito: boolean; grande: boolean; separador: boolean; celulas?: string[] | null; cabecalho?: boolean; quebraPagina?: boolean; soTalao?: boolean
  /** Relatórios em grelha: faixa no topo da grelha, linha de alerta, subtotal, n.º de colunas de texto, quadro de resumo, título, total e só A4. */
  faixa?: boolean; alerta?: boolean; subtotal?: boolean; colunasTexto?: number; cartao?: boolean; titulo?: boolean; total?: boolean; soA4?: boolean }
/** `cabecalho`: identificação do estabelecimento (ao lado do logótipo em A4; centrada nos talões). */
export interface DocumentoImpressao { titulo: string; linhas: LinhaImpressao[]; cabecalho?: LinhaImpressao[] | null }
export type TipoDocumentoRelatorio = 'Pagamentos' | 'Vendas' | 'Produtos' | 'Despesas' | 'Financeiro' | 'Stock' | 'FechoCaixa'
  | 'ListaVendas' | 'Clientes' | 'Impostos' | 'Anulacoes' | 'CartoesEvento' | 'FacturasCredito' | 'HistoricoCliente' | 'ExtratoCliente' | 'DescargasCartoes' | 'FechoDia' | 'FolhaContagem'
  | 'Acompanhamentos' | 'Existencias' | 'Transferencias' | 'Adiantamentos' | 'ExtratoAdiantamentos'
  | 'ContasPagar' | 'VendasPorHora' | 'Cotacoes' | 'Cozinha' | 'Delivery' | 'Mesas' | 'Pontos' | 'Reservas'
export interface ImpressoraRelatorio { id: number; nome: string; funcao: FuncaoImpressora; larguraPapelMm: number; modo: ModoImpressora }

// ---------- Limpeza de dados técnicos (Configurações → Sistema) ----------

export type TipoLimpeza = 'TrabalhosImpressao' | 'Logs' | 'Copias' | 'RegistoTransaccoes'
/** valor: dias (trabalhos e logs) ou cópias a manter; minimo: o mínimo aceite. */
export interface ItemLimpeza { tipo: TipoLimpeza; titulo: string; descricao: string; quantidade: number; mb: number; disponivel: boolean; motivo: string | null; minimo: number; valor: number; local: string | null }
export interface EstadoLimpeza { baseMb: number; limiteMb: number | null; registoTransaccoesMb: number | null; tabelas: { nome: string; linhas: number; mb: number }[]; itens: ItemLimpeza[] }
export interface ResultadoLimpeza { tipo: TipoLimpeza; apagados: number; mbLibertados: number; mensagem: string }
