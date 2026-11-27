// Catálogo: produtos, categorias, impostos. (reexportados por api.ts)

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
  /** Só para o stock (matérias-primas): não aparece no POS; as subcategorias de uma categoria oculta também não. */
  ocultaNoPos?: boolean
}

export interface Produto {
  unidadeMedidaId: number | null
  unidade: string
  armazemId: number | null
  armazem: string | null
  /** No POS: pede a quantidade ao lançar. */
  pedeQuantidade: boolean
  /** Código do produto na balança (PLU), lido das etiquetas com peso ou preço. */
  codigoBalanca?: number | null
  /** No POS: pede a escolha dos acompanhamentos ao lançar. */
  temAcompanhamentos?: boolean
  /** Preços nas tabelas de preço activas (as que faltam usam o preço normal). */
  precos?: { tabelaPrecoId: number; preco: number }[] | null
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

/** Tabela de preço (por zona ou «Retalho» no balcão). */
export interface TabelaPreco { id: number; nome: string; descricao: string | null; activo: boolean; produtos: number; zonas: string[]; retalho: boolean }

/** Preço do produto na tabela do pedido; sem preço nessa tabela, o preço normal. */
export function precoNaTabela(p: { precoVenda: number; precos?: { tabelaPrecoId: number; preco: number }[] | null }, tabelaPrecoId: number | null | undefined) {
  const proprio = tabelaPrecoId == null ? undefined : p.precos?.find((x) => x.tabelaPrecoId === tabelaPrecoId)?.preco
  return proprio ?? p.precoVenda
}
