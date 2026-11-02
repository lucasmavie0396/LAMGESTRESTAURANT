// Códigos de permissão usados no frontend (o catálogo completo e a verificação real estão no backend).
// No frontend servem apenas para esconder opções; a API recusa sempre o que não for permitido.

export const P = {
  dashboardVer: 'dashboard.ver',
  utilizadoresGerir: 'utilizadores.gerir',
  perfisGerir: 'perfis.gerir',
} as const

export const NOMES_MODULOS: Record<string, string> = {
  dashboard: 'Dashboard',
  utilizadores: 'Utilizadores',
  perfis: 'Perfis',
  produtos: 'Produtos',
  pos: 'POS',
  mesas: 'Mesas',
  cozinha: 'Cozinha',
  pagamentos: 'Pagamentos',
  caixa: 'Caixa',
  stock: 'Stock',
  receitas: 'Fichas técnicas',
  compras: 'Compras',
  clientes: 'Clientes',
  reservas: 'Reservas',
  delivery: 'Delivery',
  despesas: 'Despesas',
  funcionarios: 'Funcionários',
  relatorios: 'Relatórios',
  auditoria: 'Auditoria',
  configuracoes: 'Configurações',
}
