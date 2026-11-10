/** Relatórios das contas correntes dos clientes. */
export type VistaContas = 'facturas' | 'historico' | 'extrato'

export const VISTAS_CONTAS: { vista: VistaContas; titulo: string; precisaCliente: boolean }[] = [
  { vista: 'facturas', titulo: 'Facturas a crédito e dívidas', precisaCliente: false },
  { vista: 'historico', titulo: 'Histórico de facturas do cliente', precisaCliente: true },
  { vista: 'extrato', titulo: 'Extrato de conta corrente', precisaCliente: true },
]
