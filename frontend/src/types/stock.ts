// Stock e compras. (reexportados por api.ts)

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
  /** Categoria (ou subcategoria «Pai › Filho») do catálogo e localização no armazém: agrupam a folha de contagem. */
  categoriaId?: number | null
  categoria?: string | null
  localizacao?: string | null
  /** Armazém habitual (o principal, se não tiver um escolhido). */
  armazemId?: number | null
  armazem?: string | null
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

export interface InventarioLinha {
  id: number; artigoId: number; codigo: string; artigo: string; unidade: string; quantidadeSistema: number; quantidadeContada: number; diferenca: number
  tipo?: TipoArtigo; categoriaId?: number | null; categoria?: string | null; subcategoria?: string | null; localizacao?: string | null
}
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
export interface ReceitaLinha { artigoId: number; codigo: string; artigo: string; quantidade: number; unidadeMedidaId: number; unidade: string; custoEstimado: number; armazemId?: number | null; armazem?: string | null }
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
