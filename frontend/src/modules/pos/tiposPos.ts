import type { Mesa, PedidoItem, Produto, TipoPedido } from '../../types/api'

/** Nome do tipo de pedido no ecrã. */
export function nomeTipo(tipo: TipoPedido, mesa?: string | null) {
  return tipo === 'Mesa' ? `Mesa ${mesa}` : tipo === 'TakeAway' ? 'Take Away' : tipo === 'Balcao' ? 'Balcão' : 'Delivery'
}

/** Pedido novo ainda sem itens: só existe no servidor quando se adiciona o primeiro produto. */
export interface Rascunho {
  tipo: TipoPedido
  mesa?: Mesa
  nome?: string
  contacto?: string
  endereco?: string
  confirmarReservada?: boolean
  /** Chave de idempotência da criação: mantém-se até o pedido ser criado (reenvios não duplicam). */
  chave: string
}

/** Diálogo aberto no POS. */
export type Dialogo =
  | { tipo: 'mesa' }
  | { tipo: 'transferir' }
  | { tipo: 'passar-mesa' }
  | { tipo: 'entrega'; pedido: 'TakeAway' | 'Delivery' }
  | { tipo: 'observacoes'; item: PedidoItem }
  | { tipo: 'comentario-pedido' }
  | { tipo: 'cancelar-item'; item: PedidoItem }
  | { tipo: 'cancelar-pedido' }
  | { tipo: 'desconto' }
  | { tipo: 'pagamento' }
  | { tipo: 'pagamento-fatura' }
  | { tipo: 'abertos' }
  | { tipo: 'estorno' }
  | { tipo: 'cliente' }
  | { tipo: 'reservada'; mesa: Mesa }
  | { tipo: 'quantidade'; produto: Produto }
  | { tipo: 'acompanhamentos'; produto: Produto; quantidade: number }
  | null
