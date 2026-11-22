import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { ModalPagamento } from './ModalPagamento'
import { desenhar, RespostaHttp, servidorFalso } from '../../testes/servidorFalso'
import type { MetodoPagamento, Pedido } from '../../types/api'

const metodos = [
  { id: 1, nome: 'Numerário', codigo: 'NUMERARIO', activo: true, permiteTroco: true, exigeReferencia: false },
  { id: 2, nome: 'M-Pesa', codigo: 'MPESA', activo: true, permiteTroco: false, exigeReferencia: true },
] as unknown as MetodoPagamento[]

const pedido = {
  id: 7, numero: 'P-0007', total: 450.5, totalPago: 0, emDivida: 450.5, clienteId: null, clienteNome: null,
} as unknown as Pedido

// Separadores de milhares do Intl (espaço inseparável U+00A0 ou fino U+202F) passam a espaço normal.
const ESPACOS = new RegExp('[\u00a0\u202f]', 'g')
const normalizar = (s: string | null) => (s ?? '').replace(ESPACOS, ' ')

describe('pagamento no POS', () => {
  beforeEach(() => {
    localStorage.setItem('lamgest.terminalId', '3')
  })
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    localStorage.clear()
  })

  function servidor(pagamento: (corpo: unknown, chave: string) => unknown = () => ({ ...pedido, totalPago: 450.5, emDivida: 0, estado: 'Pago' })) {
    return servidorFalso({
      'GET /api/caixa/terminais/3/sessao-aberta': { id: 11, terminal: 'Caixa 1' },
      'GET /api/caixa/metodos-pagamento': metodos,
      'POST /api/pedidos/7/pagamentos/multiplos': (p: { corpo: unknown; cabecalhos: Record<string, string> }) => pagamento(p.corpo, p.cabecalhos['Idempotency-Key']),
    })
  }

  it('numerário com troco: mostra o troco e envia o valor recebido com a chave de idempotência', async () => {
    const { pedidos } = servidor()
    const aoPagar = vi.fn()
    desenhar(<ModalPagamento pedido={pedido} aoPagar={aoPagar} aoFechar={() => {}} />)

    const recebido = await screen.findByLabelText('Valor recebido (MT)')
    fireEvent.change(recebido, { target: { value: '500' } })
    expect(normalizar(screen.getByText(/^Troco:/).textContent)).toBe('Troco: 49,50 MT')

    const confirmar = screen.getByRole('button', { name: /Confirmar/ })
    await waitFor(() => expect(confirmar).toBeEnabled()) // depois de confirmar a ligação ao servidor
    fireEvent.click(confirmar)
    await waitFor(() => expect(aoPagar).toHaveBeenCalledOnce())

    const envio = pedidos.find((p) => p.metodo === 'POST')!
    expect(envio.corpo).toEqual({
      sessaoCaixaId: 11,
      linhas: [{ metodoPagamentoId: 1, valor: 450.5, valorRecebido: 500, referencia: null }],
      reciboNoDispositivo: false,
      clienteId: null,
    })
    expect(envio.cabecalhos['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
  })

  it('M-Pesa exige a referência e recebido inferior ao valor bloqueia a confirmação', async () => {
    servidor()
    desenhar(<ModalPagamento pedido={pedido} aoPagar={() => {}} aoFechar={() => {}} />)
    const recebido = await screen.findByLabelText('Valor recebido (MT)')
    fireEvent.change(recebido, { target: { value: '400' } })
    expect(screen.getByText('O valor recebido é inferior ao valor a pagar.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Confirmar/ })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'M-Pesa' }))
    expect(screen.getByText('Indique a referência M-Pesa.')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Referência da transacção'), { target: { value: 'ABC123' } })
    await waitFor(() => expect(screen.getByRole('button', { name: /Confirmar/ })).toBeEnabled())
  })

  it('pagamento dividido em dois métodos não pode exceder o saldo em dívida', async () => {
    servidor()
    desenhar(<ModalPagamento pedido={pedido} aoPagar={() => {}} aoFechar={() => {}} />)
    const valor = await screen.findByLabelText('Valor (MT)')
    fireEvent.change(valor, { target: { value: '300' } })
    fireEvent.click(screen.getByRole('button', { name: /Juntar outro método/ }))
    // A segunda linha propõe o que falta (150,50) com o método seguinte.
    const valores = screen.getAllByLabelText('Valor (MT)')
    expect(valores[1]).toHaveValue(150.5)
    fireEvent.change(valores[1], { target: { value: '200' } })
    fireEvent.change(screen.getByLabelText('Referência da transacção'), { target: { value: 'MP-1' } })
    expect(normalizar(screen.getByText(/excede o saldo em dívida/).textContent)).toContain('500,00 MT')
  })

  it('erro de rede mantém a mesma chave: repetir não duplica o pagamento', async () => {
    const chaves: string[] = []
    let tentativa = 0
    servidor((_, chave) => {
      chaves.push(chave)
      if (++tentativa === 1) return new RespostaHttp(500, { title: 'Erro', detail: 'Falha momentânea.' })
      return { ...pedido, totalPago: 450.5, emDivida: 0, estado: 'Pago' }
    })
    const aoPagar = vi.fn()
    desenhar(<ModalPagamento pedido={pedido} aoPagar={aoPagar} aoFechar={() => {}} />)
    const confirmar = await screen.findByRole('button', { name: /Confirmar/ })
    await waitFor(() => expect(confirmar).toBeEnabled())
    fireEvent.click(confirmar)
    expect(await screen.findByText('Falha momentânea.')).toBeInTheDocument()
    fireEvent.click(confirmar)
    await waitFor(() => expect(aoPagar).toHaveBeenCalledOnce())
    expect(chaves).toHaveLength(2)
    expect(chaves[1]).toBe(chaves[0])
  })

  it('sem sessão de caixa aberta no terminal, avisa e não deixa confirmar', async () => {
    servidorFalso({
      'GET /api/caixa/terminais/3/sessao-aberta': new RespostaHttp(204),
      'GET /api/caixa/metodos-pagamento': metodos,
    })
    desenhar(<ModalPagamento pedido={pedido} aoPagar={() => {}} aoFechar={() => {}} />)
    expect(await screen.findByText('Caixa não aberto')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Confirmar/ })).toBeDisabled()
  })
})
