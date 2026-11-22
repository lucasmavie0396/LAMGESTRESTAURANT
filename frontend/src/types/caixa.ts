// Caixa e dia de operação. (reexportados por api.ts)

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
  cortarPapel?: boolean
  abrirGaveta?: boolean
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

// ---------- Dia de operação (Abrir Dia / Fechar Dia) ----------

export type EstadoDia = 'Aberto' | 'Fechado'
export interface DiaOperacao {
  id: number
  data: string
  estado: EstadoDia
  numero: string | null
  abertoPor: string
  abertoEmUtc: string
  fechadoPor: string | null
  fechadoEmUtc: string | null
  caixas: number
  caixasAbertos: number
  rowVersion: string
}
/** `exigir`: os caixas só abrem com o dia aberto. `hoje`: data de hoje no fuso do servidor. */
export interface EstadoDiaOperacao { exigir: boolean; aberto: DiaOperacao | null; ultimoFechado: DiaOperacao | null; hoje: string }
export interface CaixaDoDia {
  sessaoId: number; terminal: string; operador: string; abertaEmUtc: string; fechadaEmUtc: string | null; aberta: boolean
  pedidos: number; recebido: number; fundo: number; esperado: number | null; contado: number | null; diferenca: number | null
}
export interface ResumoDia {
  diaId: number; numero: string | null; data: string; estado: EstadoDia; abertoPor: string; abertoEmUtc: string; fechadoPor: string | null
  fechadoEmUtc: string | null; observacoes: string | null; pedidos: number; vendasBrutas: number; descontos: number; taxasEntrega: number
  vendasLiquidas: number; impostos: number; ticketMedio: number; totalRecebido: number; totalEntrado: number; totalSaido: number; diferenca: number | null
  caixas: CaixaDoDia[]
  metodos: { metodo: string; porCaixa: number[]; total: number }[]
  movimentos: { tipo: TipoMovimentoCaixa; quantidade: number; valor: number }[]
  categorias: { categoria: string; quantidade: number; total: number; produtos: { produto: string; quantidade: number; total: number }[] }[]
  controlo: {
    descontos: number; valorDescontos: number; itensAnulados: number; valorItensAnulados: number; estornos: number; valorEstornos: number
    pagamentosAnulados: number; valorPagamentosAnulados: number; pedidosEmAberto: number
  }
  pendencias: string[]
  /** Contas ainda abertas (no fecho, transitam para o dia seguinte). */
  pedidosAbertos?: { pedidoId: number; numero: string; local: string; responsavel: string | null; total: number; pago: number }[] | null
}
