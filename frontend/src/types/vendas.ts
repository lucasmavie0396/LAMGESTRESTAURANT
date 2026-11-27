// Pedidos, pagamentos e POS. (reexportados por api.ts)

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
  /** Acompanhamentos escolhidos («Arroz, Salada fria»), sem preço. */
  acompanhamentos?: string | null
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
  /** Utilizador responsável (quem abriu a mesa, ou a quem foi passada). */
  responsavelId?: number | null
  responsavel?: string | null
  /** Tabela de preço dos itens novos (zona da mesa ou retalho); vazio = preço normal. */
  tabelaPrecoId?: number | null
  tabelaPreco?: string | null
  numeroPessoas: number
  nomeEntrega?: string | null
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
  nomeEntrega?: string | null
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
  acompanhamentos?: string | null
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
