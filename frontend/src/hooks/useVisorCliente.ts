import { useEffect, useRef } from 'react'
import { api } from '../api/cliente'
import type { Pedido } from '../types/api'
import { emPda } from '../utils/pda'
import { lerTerminalLocal } from '../utils/terminalLocal'

/** Depois de pago, o troco e o agradecimento ficam no visor pelo menos este tempo (mesmo que o POS passe logo à venda seguinte). */
export const MOSTRAR_PAGO_MS = 10_000
const ATRASO_MS = 250

/**
 * Visor do cliente deste posto: avisa o servidor do pedido que está no ecrã do POS (e do valor recebido durante o pagamento).
 * O servidor compõe o que o cliente vê a partir do pedido gravado; sem visor configurado no posto, o aviso não faz nada.
 * Nos tablets e PDAs (app de venda) não se envia: o visor é o do posto do caixa.
 */
export function useVisorCliente(pedido: Pedido | null, recebido: number | null) {
  const pagoEm = useRef(0)
  const terminal = emPda() ? null : lerTerminalLocal()
  const pedidoId = pedido && pedido.estado !== 'Cancelado' ? pedido.id : null
  const pago = pedido?.estado === 'Pago'
  // Muda quando o pedido muda (itens, descontos, pagamentos): a versão gravada muda a cada alteração.
  const chave = pedidoId === null ? 'vazio' : `${pedidoId}|${pedido?.rowVersion}|${pedido?.estado}|${pago ? '' : (recebido ?? '')}`

  useEffect(() => {
    if (terminal === null) return
    const atraso = pedidoId === null ? Math.max(ATRASO_MS, pagoEm.current + MOSTRAR_PAGO_MS - Date.now()) : ATRASO_MS
    const id = window.setTimeout(() => {
      if (pago) pagoEm.current = Date.now()
      void api.post('/api/visor', { terminalId: terminal, pedidoId, recebido: pago || pedidoId === null ? null : recebido }).catch(() => undefined)
    }, atraso)
    return () => window.clearTimeout(id)
    // A chave resume o pedido e o recebido.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chave, terminal])

  // Ao sair do POS, o visor volta à mensagem de boas-vindas.
  useEffect(() => {
    if (terminal === null) return
    return () => { void api.post('/api/visor', { terminalId: terminal, pedidoId: null, recebido: null }).catch(() => undefined) }
  }, [terminal])

  /** Pedido acabado de pagar: mostra logo o troco e o agradecimento (no Balcão o ecrã passa logo à venda seguinte). */
  return {
    pago: (p: Pedido) => {
      if (terminal === null || p.estado !== 'Pago') return
      pagoEm.current = Date.now()
      void api.post('/api/visor', { terminalId: terminal, pedidoId: p.id, recebido: null }).catch(() => undefined)
    },
  }
}
