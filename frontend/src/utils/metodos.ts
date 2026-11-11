import type { MetodoPagamento } from '../types/api'

export const FATURA_A_CREDITO = 'FACTURA_CREDITO'
export const CARTAO_EVENTO = 'CARTAO_EVENTO'

/** Métodos em que dinheiro entra ou sai de facto (exclui a Fatura a Crédito). */
export const naoCredito = (m: MetodoPagamento) => m.codigo !== FATURA_A_CREDITO

/** Dinheiro real: nem Fatura a Crédito nem saldo de Cartão Evento. */
export const metodoDeDinheiro = (m: MetodoPagamento) => naoCredito(m) && m.codigo !== CARTAO_EVENTO
