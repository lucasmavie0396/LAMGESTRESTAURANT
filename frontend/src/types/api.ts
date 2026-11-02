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
}

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
}

export interface Produto {
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
export type TipoMovimentoCaixa = 'Entrada' | 'Saida' | 'Despesa'

export interface Terminal {
  id: number
  nome: string
  descricao: string | null
  activo: boolean
  sessaoAbertaId: number | null
  sessaoAbertaPor: string | null
}

export interface MetodoPagamento {
  id: number
  codigo: string
  nome: string
  permiteTroco: boolean
  exigeReferencia: boolean
  contaNaGaveta: boolean
  activo: boolean
}

export interface ResumoMetodo {
  metodoPagamentoId: number
  metodo: string
  contaNaGaveta: boolean
  fundo: number
  recebimentos: number
  entradas: number
  saidas: number
  esperado: number
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
  totalRecebido: number
  totalEsperado: number
  totalContado: number | null
  totalDiferenca: number | null
  porMetodo: ResumoMetodo[]
  movimentos: MovimentoCaixa[]
  rowVersion: string
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
