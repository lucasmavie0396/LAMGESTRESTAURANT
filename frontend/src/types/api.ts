// Contratos da API (espelho dos DTOs do backend).

export interface Pagina<T> {
  itens: T[]
  total: number
  numeroPagina: number
  tamanhoPagina: number
}

export interface SessaoUtilizador {
  id: number
  nomeUtilizador: string
  nomeCompleto: string
  perfis: string[]
  permissoes: string[]
  deveAlterarPalavraPasse: boolean
}

export interface EstadoSaude {
  api: boolean
  baseDados: boolean
  migracoesPendentes: boolean
  horaServidorUtc: string
}

export interface UtilizadorResumo {
  id: number
  nomeUtilizador: string
  nomeCompleto: string
  activo: boolean
  deveAlterarPalavraPasse: boolean
  bloqueadoAteUtc: string | null
  ultimoLoginUtc: string | null
  perfis: string[]
  /** Tem código de entrada (PIN) para o ecrã táctil. */
  temPin: boolean
  /** Aparece como botão no ecrã de entrada táctil. */
  ecraTactil: boolean
}

/** Valida; AExpirar (≤ 30 dias); Tolerancia (expirou há ≤ 7 dias); Expirada, Inexistente e Invalida bloqueiam as gravações. */
export type EstadoLicenca = 'Valida' | 'AExpirar' | 'Tolerancia' | 'Expirada' | 'Inexistente' | 'Invalida'
export interface EstadoLicencaDto {
  estado: EstadoLicenca
  bloqueada: boolean
  mensagem: string
  nomeComercial: string | null
  nuit: string | null
  validaAte: string | null
  diasRestantes: number | null
}
export interface LicencaDto { licencaId: string; nomeComercial: string; nuit: string; emitidaEm: string; validaAte: string; activadaEmUtc: string; activadaPor: string | null }

/** Botão do ecrã de entrada táctil. */
export interface UtilizadorTactil { id: number; nome: string }

export interface PerfilResumo {
  id: number
  nome: string
  descricao: string | null
  sistema: boolean
  numeroUtilizadores: number
  permissoes: string[]
}

export interface PermissaoInfo {
  codigo: string
  modulo: string
  descricao: string
}

// ---------- Catálogo ----------

export type TipoProduto = 'Restaurante' | 'Bar' | 'Outro'

export interface Categoria {
  id: number
  nome: string
  descricao: string | null
  categoriaPaiId: number | null
  categoriaPaiNome: string | null
  estacaoPreparacaoId: number | null
  estacaoPreparacaoNome: string | null
  cor: string | null
  ordem: number
  activo: boolean
  numeroProdutosActivos: number
  /** Impressora dos talões; vazio = herda da categoria principal (ou regras automáticas). */
  impressoraId: number | null
  impressoraNome: string | null
}

export interface Produto {
  unidadeMedidaId: number | null
  unidade: string
  armazemId: number | null
  armazem: string | null
  /** No POS: pede a quantidade ao lançar. */
  pedeQuantidade: boolean
  id: number
  codigo: string
  codigoBarras: string | null
  nome: string
  descricao: string | null
  categoriaId: number
  categoriaNome: string
  tipo: TipoProduto
  precoVenda: number
  custo: number
  taxaImpostoId: number | null
  taxaImpostoNome: string | null
  taxaImpostoPercentagem: number | null
  imagemUrl: string | null
  enviaCozinha: boolean
  disponivel: boolean
  activo: boolean
  /** Versão (base64) para concorrência optimista. */
  rowVersion: string
  /** Impressora dos talões; vazio = a da categoria. */
  impressoraId: number | null
  impressoraNome: string | null
  /** A venda baixa o stock (artigo e ficha técnica 1:1 criados automaticamente). */
  controlaStock: boolean
  /** Artigo de stock do produto (ficha técnica 1:1), se existir. */
  artigoId: number | null
}

export interface TaxaImposto {
  id: number
  nome: string
  percentagem: number
  activo: boolean
}

export interface EstacaoPreparacao {
  id: number
  nome: string
  activo: boolean
  armazemId: number | null
  armazem: string | null
}

// ---------- Sala ----------

export type EstadoMesa = 'Livre' | 'Ocupada' | 'Reservada' | 'Indisponivel'

export interface Zona {
  id: number
  nome: string
  ordem: number
  activo: boolean
  numeroMesas: number
}

export interface Mesa {
  id: number
  nome: string
  zonaId: number
  zonaNome: string
  capacidade: number
  estado: EstadoMesa
  ocupadaDesdeUtc: string | null
  activo: boolean
  pedidosAbertos: number
  totalAberto: number
  rowVersion: string
}

// ---------- Caixa ----------

export type EstadoSessaoCaixa = 'Aberta' | 'Fechada'
export type TipoMovimentoCaixa = 'Entrada' | 'Saida' | 'Despesa' | 'Reembolso'

export interface Terminal {
  id: number
  nome: string
  descricao: string | null
  activo: boolean
  sessaoAbertaId: number | null
  sessaoAbertaPor: string | null
  /** Número do posto usado pelo instalador do computador (atalho com ?posto=N). */
  numero: number | null
  /** Agente de impressão do posto (instalado com o LAMGest-Setup, modo Posto). */
  agenteRegistado?: boolean
  agenteVistoEmUtc?: string | null
  agenteVersao?: string | null
  agenteImpressoras?: string[] | null
}

export interface PostoPublico { numero: number; terminalId: number; nome: string }

export interface MetodoPagamento {
  id: number
  codigo: string
  nome: string
  permiteTroco: boolean
  exigeReferencia: boolean
  contaNaGaveta: boolean
  activo: boolean
  ordem: number
}

/** Producao = outro centro de produção (pastelaria, grelhados…). */
export type FuncaoImpressora = 'Caixa' | 'Bar' | 'Cozinha' | 'Producao' | 'Etiquetas'
/** Directo: o servidor imprime sem diálogo. Pdf: o documento abre em PDF no ecrã de quem o pediu. */
export type ModoImpressora = 'Directo' | 'Pdf'
export interface Impressora {
  id: number
  nome: string
  funcao: FuncaoImpressora
  /** Nome exacto da impressora no Windows do servidor (vazio em modo PDF sem impressora). */
  nomeWindows: string
  /** 58 ou 80 (talão) ou 210 (A4). */
  larguraPapelMm: number
  modo: ModoImpressora
  estacaoPreparacaoId: number | null
  estacao: string | null
  terminalId: number | null
  terminal: string | null
  activo: boolean
  ultimaImpressaoUtc: string | null
  ultimoErro: string | null
  /** Onde está instalada: null = Windows do servidor; senão o terminal (posto) com o agente de impressão. */
  postoId: number | null
  posto: string | null
}

/** Servidor (postoId null) ou posto com as impressoras instaladas no seu Windows (enviadas pelo agente de impressão). */
export interface LocalImpressoras {
  postoId: number | null
  numero: number | null
  nome: string
  temAgente: boolean
  ligado: boolean
  vistoEmUtc: string | null
  versao: string | null
  impressoras: string[]
}

export interface ResumoMetodo {
  metodoPagamentoId: number
  metodo: string
  contaNaGaveta: boolean
  /** Tem de ser contado no fecho. */
  contar: boolean
  fundo: number
  // Fecho cego: vazios (null) enquanto a sessão está aberta; o servidor só os revela depois do fecho.
  recebimentos: number | null
  entradas: number | null
  saidas: number | null
  esperado: number | null
  contado: number | null
  diferenca: number | null
}

export interface MovimentoCaixa {
  id: number
  tipo: TipoMovimentoCaixa
  metodo: string
  valor: number
  motivo: string
  utilizador: string | null
  dataUtc: string
}

export interface SessaoCaixa {
  id: number
  terminalId: number
  terminal: string
  estado: EstadoSessaoCaixa
  abertaPor: string
  abertaEmUtc: string
  fundoInicial: number
  observacoesAbertura: string | null
  fechadaPor: string | null
  fechadaEmUtc: string | null
  observacoesFecho: string | null
  pedidosPagos: number
  /** Vazios enquanto a sessão está aberta (fecho cego). */
  totalRecebido: number | null
  totalEsperado: number | null
  totalContado: number | null
  totalDiferenca: number | null
  porMetodo: ResumoMetodo[]
  movimentos: MovimentoCaixa[]
  rowVersion: string
}

// ---------- Vendas ----------

export type TipoPedido = 'Mesa' | 'TakeAway' | 'Delivery' | 'Balcao'
export type EstadoPedido = 'Aberto' | 'Pago' | 'Cancelado' | 'Estornado'
export type EstadoItemCozinha = 'NaoEnviado' | 'Pendente' | 'EmPreparacao' | 'Pronto' | 'Entregue'
export type EstadoEntrega = 'Recebido' | 'EmPreparacao' | 'Pronto' | 'SaiuParaEntrega' | 'Entregue' | 'Cancelado'

export interface PedidoItem {
  unidade: string
  id: number
  produtoId: number
  nomeProduto: string
  precoUnitario: number
  quantidade: number
  total: number
  observacoes: string | null
  enviaCozinha: boolean
  estadoCozinha: EstadoItemCozinha
  enviadoCozinhaEmUtc: string | null
  cancelado: boolean
  motivoCancelamento: string | null
}

export interface Pagamento {
  id: number
  metodo: string
  valor: number
  valorRecebido: number
  troco: number
  referencia: string | null
  estado: 'Valido' | 'Anulado'
  dataUtc: string
  utilizador: string | null
  motivoAnulacao: string | null
}

export interface Pedido {
  id: number
  numero: string
  tipo: TipoPedido
  estado: EstadoPedido
  estadoEntrega: EstadoEntrega | null
  mesaId: number | null
  mesaNome: string | null
  clienteId: number | null
  clienteNome: string | null
  garcomId: number | null
  garcomNome: string | null
  numeroPessoas: number
  contactoEntrega: string | null
  enderecoEntrega: string | null
  observacoes: string | null
  subtotal: number
  desconto: number
  impostos: number
  taxaEntrega: number
  total: number
  totalPago: number
  emDivida: number
  criadoEmUtc: string
  criadoPor: string | null
  fechadoEmUtc: string | null
  reciboNumero: string | null
  motivoCancelamento: string | null
  itens: PedidoItem[]
  pagamentos: Pagamento[]
  rowVersion: string
}

export interface PedidoResumo {
  id: number
  numero: string
  tipo: TipoPedido
  estado: EstadoPedido
  estadoEntrega: EstadoEntrega | null
  mesaId: number | null
  mesaNome: string | null
  contactoEntrega: string | null
  enderecoEntrega: string | null
  total: number
  totalPago: number
  numeroItens: number
  itensPorEnviar: number
  itensProntos: number
  criadoEmUtc: string
}

export interface ReciboLinha {
  produto: string
  quantidade: number
  precoUnitario: number
  total: number
}

export interface Recibo {
  numero: string
  pedidoNumero: string
  emitidoEmUtc: string
  estabelecimento: string
  endereco: string | null
  telefone: string | null
  nuit: string | null
  mesa: string | null
  tipo: TipoPedido
  operador: string | null
  linhas: ReciboLinha[]
  subtotal: number
  desconto: number
  impostos: number
  taxaEntrega: number
  total: number
  pagamentos: Pagamento[]
  moeda: string
  rodape: string | null
}

export interface ItemCozinha {
  id: number
  produto: string
  quantidade: number
  observacoes: string | null
  estado: EstadoItemCozinha
  enviadoEmUtc: string
  estacaoId: number | null
  estacao: string | null
}

export interface TicketCozinha {
  pedidoId: number
  numero: string
  tipo: TipoPedido
  mesa: string | null
  observacoes: string | null
  primeiroEnvioUtc: string
  itens: ItemCozinha[]
}

// ---------- Stock ----------

export type TipoArtigo = 'Ingrediente' | 'Revenda' | 'Consumivel'
export type Dimensao = 'Unidade' | 'Massa' | 'Volume'
export type TipoMovimentoStock = 'EntradaCompra' | 'EstornoCompra' | 'SaidaVenda' | 'EstornoVenda' | 'Ajuste' | 'Perda' | 'TransferenciaSaida' | 'TransferenciaEntrada' | 'Inventario' | 'StockInicial'

/** Impressora de talões (Bar/Cozinha) para escolher na categoria e no produto. */
export interface ImpressoraTalao { id: number; nome: string; funcao: 'Bar' | 'Cozinha' | 'Producao'; activo: boolean }

export interface ImagemEncontrada { titulo: string; miniatura: string; url: string; autor: string | null; licenca: string; pagina: string | null; largura: number | null; altura: number | null }
export interface ImagemGuardada { url: string; largura: number; altura: number; autor: string | null; licenca: string | null }

export interface Unidade { activo: boolean; id: number; codigo: string; nome: string; dimensao: Dimensao; factorParaBase: number }
export interface Armazem { id: number; nome: string; principal: boolean; activo: boolean }

export interface Artigo {
  id: number
  codigo: string
  nome: string
  tipo: TipoArtigo
  unidadeMedidaId: number
  unidade: string
  dimensao: Dimensao
  stockMinimo: number
  custoMedio: number
  ultimoCusto: number
  permiteStockNegativo: boolean
  activo: boolean
  saldo: number
  abaixoMinimo: boolean
  valorStock: number
  porArmazem: { armazemId: number; armazem: string; quantidade: number }[]
}

export interface MovimentoStock {
  id: number
  dataUtc: string
  artigoId: number
  artigo: string
  unidade: string
  armazem: string
  tipo: TipoMovimentoStock
  quantidade: number
  custoUnitario: number
  origemTipo: string | null
  origemId: number | null
  motivo: string | null
  utilizador: string | null
}

export interface InventarioLinha { id: number; artigoId: number; codigo: string; artigo: string; unidade: string; quantidadeSistema: number; quantidadeContada: number; diferenca: number }
export interface Inventario {
  id: number
  armazemId: number
  armazem: string
  estado: 'Aberto' | 'Confirmado' | 'Cancelado'
  criadoEmUtc: string
  criadoPor: string | null
  confirmadoEmUtc: string | null
  confirmadoPor: string | null
  observacoes: string | null
  linhas: InventarioLinha[]
  rowVersion: string
}
export interface InventarioResumo { id: number; armazem: string; estado: Inventario['estado']; criadoEmUtc: string; confirmadoEmUtc: string | null; linhas: number; comDiferenca: number }

export interface ProdutoFicha { produtoId: number; codigo: string; produto: string; categoria: string; precoVenda: number; activo: boolean; versaoActiva: number | null; custoEstimado: number | null; numeroIngredientes: number }
export interface ReceitaLinha { artigoId: number; codigo: string; artigo: string; quantidade: number; unidadeMedidaId: number; unidade: string; custoEstimado: number }
export interface Receita {
  id: number
  produtoId: number
  produto: string
  precoVenda: number
  versao: number
  activa: boolean
  validaDesdeUtc: string
  validaAteUtc: string | null
  observacoes: string | null
  custoEstimado: number
  margemPercentagem: number | null
  linhas: ReceitaLinha[]
}

// ---------- Compras ----------

export type EstadoCompra = 'Rascunho' | 'Confirmada' | 'Anulada'
export interface Fornecedor { id: number; nome: string; nuit: string | null; telefone: string | null; email: string | null; endereco: string | null; observacoes: string | null; activo: boolean; numeroCompras: number; totalCompras: number }
export interface CompraItem { id: number; artigoId: number; codigo: string; artigo: string; quantidade: number; unidadeMedidaId: number; unidade: string; custoUnitario: number; desconto: number; taxaImpostoPercentagem: number; valorImposto: number; total: number }
export interface Compra {
  id: number
  fornecedorId: number
  fornecedor: string
  numeroDocumento: string
  dataDocumento: string
  dataVencimento: string | null
  armazemId: number
  armazem: string
  estado: EstadoCompra
  subtotal: number
  desconto: number
  impostos: number
  total: number
  observacoes: string | null
  criadoPor: string | null
  confirmadaEmUtc: string | null
  confirmadaPor: string | null
  anuladaEmUtc: string | null
  motivoAnulacao: string | null
  itens: CompraItem[]
  rowVersion: string
}
export interface CompraResumo { id: number; fornecedor: string; numeroDocumento: string; dataDocumento: string; estado: EstadoCompra; total: number; numeroItens: number }

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

export interface OpcoesPos { mesa: boolean; takeAway: boolean; delivery: boolean; balcao: boolean; codigoBarras: boolean }
export interface ConfiguracoesPublicas {
  nomeEstabelecimento: string
  moeda: string
  simboloMoeda: string
  fusoHorario: string
  logotipo: string | null
  pos: OpcoesPos | null
  logotipo80mm: boolean
  /** Cópias por documento (conta, venda-dinheiro, fecho…). */
  copias: Record<string, number> | null
}

/** Documento imprimível (recibos e relatórios), igual ao que o servidor envia às impressoras. */
/** Linha de documento impresso. Com `celulas`: linha de tabela (1.ª célula = descrição, restantes = valores); `cabecalho` = títulos das colunas. */
export interface LinhaImpressao { texto: string; direita: string | null; alinhamento: 'Esquerda' | 'Centro' | 'Direita'; negrito: boolean; grande: boolean; separador: boolean; celulas?: string[] | null; cabecalho?: boolean }
/** `cabecalho`: identificação do estabelecimento (ao lado do logótipo em A4; centrada nos talões). */
export interface DocumentoImpressao { titulo: string; linhas: LinhaImpressao[]; cabecalho?: LinhaImpressao[] | null }
export type TipoDocumentoRelatorio = 'Pagamentos' | 'Vendas' | 'Produtos' | 'Despesas' | 'Financeiro' | 'Stock' | 'FechoCaixa'
  | 'ListaVendas' | 'Clientes' | 'Impostos' | 'Anulacoes' | 'CartoesEvento' | 'FacturasCredito' | 'HistoricoCliente' | 'ExtratoCliente'
export interface ImpressoraRelatorio { id: number; nome: string; funcao: FuncaoImpressora; larguraPapelMm: number; modo: ModoImpressora }

// ---------- Relatórios ----------

export interface LinhaValor { rotulo: string; valor: number; quantidade: number }
export interface VendaDia { dia: string; total: number; pedidos: number }
export interface VendaProduto { produtoId: number; produto: string; categoria: string; quantidade: number; total: number; custoEstimado: number; codigo: string }
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

export interface ArtigoValor { artigoId: number; codigo: string; artigo: string; unidade: string; quantidade: number; valor: number }
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
export type TipoMovimentoCartao = 'Carregamento' | 'Recarga' | 'Pagamento' | 'AnulacaoPagamento' | 'Estorno'
export interface CartaoEvento { id: number; numero: string; titular: string | null; saldo: number; activo: boolean; criadoEmUtc: string; ultimoMovimentoUtc: string | null; rowVersion: string }
export interface MovimentoCartao { id: number; dataUtc: string; tipo: TipoMovimentoCartao; valor: number; saldoApos: number; metodo: string | null; pedido: string | null; motivo: string | null; operador: string | null }
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
