// Folha de contagem do inventário: ordens dos artigos (iguais no papel e no ecrã do inventário).
export type OrdemFolhaContagem = 'Alfabetica' | 'Codigo' | 'Categoria' | 'Tipo' | 'Localizacao'
export const ORDENS: { valor: OrdemFolhaContagem; rotulo: string }[] = [
  { valor: 'Alfabetica', rotulo: 'Alfabética' },
  { valor: 'Codigo', rotulo: 'Por código' },
  { valor: 'Categoria', rotulo: 'Por categoria e subcategoria' },
  { valor: 'Tipo', rotulo: 'Por tipo de artigo' },
  { valor: 'Localizacao', rotulo: 'Por localização no armazém' },
]
