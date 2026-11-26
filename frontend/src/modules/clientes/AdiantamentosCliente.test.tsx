import { afterEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, screen, within } from '@testing-library/react'
import { AdiantamentosCliente } from './AdiantamentosCliente'
import { AvisosProvider } from '../../contexts/AvisosContext'
import { desenhar, servidorFalso } from '../../testes/servidorFalso'
import type { ExtratoAdiantamentos } from '../../types/api'

const permissoes = vi.hoisted(() => ({ lista: new Set<string>() }))
vi.mock('../../contexts/SessaoContext', () => ({ useSessao: () => ({ tem: (p: string) => permissoes.lista.has(p) }) }))

const cliente = { id: 5, nome: 'Ana Fiel', activo: true }
const extrato: ExtratoAdiantamentos = {
  clienteId: 5, clienteNome: 'Ana Fiel', clienteNuit: null, clienteTelefone: null, clienteActivo: true, desde: '2026-10-27', ate: '2026-11-26',
  saldoAnterior: 0, recargas: 1000, consumos: 450.5, outros: 0, saldoFinal: 549.5, cortado: false, saldoActual: 549.5,
  cartoes: [{ id: 3, numero: '812345678901', saldo: 549.5, activo: true, ultimoMovimentoUtc: '2026-11-26T10:00:00Z' }],
  movimentos: [
    { id: 1, dataUtc: '2026-11-25T09:00:00Z', cartao: '812345678901', tipo: 'Recarga', valor: 1000, saldo: 1000, metodo: 'Dinheiro', pedido: null, motivo: null, operador: 'Gerente' },
    { id: 2, dataUtc: '2026-11-26T10:00:00Z', cartao: '812345678901', tipo: 'Pagamento', valor: -450.5, saldo: 549.5, metodo: null, pedido: 'P2026-000010', motivo: null, operador: 'Gerente' },
  ],
}
const outroCartao = { id: 9, numero: '899999999999', titular: null, saldo: 100, activo: true, criadoEmUtc: '2026-11-20T10:00:00Z', ultimoMovimentoUtc: null, rowVersion: 'AQ==', clienteId: null, clienteNome: null }

function abrir() {
  desenhar(<AvisosProvider><AdiantamentosCliente cliente={cliente} /></AvisosProvider>)
}

describe('adiantamentos na ficha do cliente', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    permissoes.lista.clear()
  })

  it('mostra o saldo por consumir, a posição global com a dívida e o extrato', async () => {
    permissoes.lista = new Set(['cartoes.ver', 'cartoes.gerir', 'facturacao.ver'])
    servidorFalso({
      'GET /api/cartoes/clientes/5/extrato': extrato,
      'GET /api/documentos/conta-corrente/5': { clienteId: 5, clienteNome: 'Ana Fiel', clienteNuit: null, totalEmDivida: 200, totalVencido: 0, facturas: [] },
    })
    abrir()
    const seccao = await screen.findByRole('region', { name: 'Adiantamentos' })
    expect(await within(seccao).findByText(/posição global/)).toBeInTheDocument()
    expect(within(seccao).getByText('812345678901', { selector: 'span' })).toBeInTheDocument()
    expect(within(seccao).getByRole('button', { name: /Recarregar/ })).toBeInTheDocument()
    expect(within(seccao).getByText(/P2026-000010/)).toBeInTheDocument()
    // Sem a permissão de associar, não há botões para cartões novos nem para associar.
    expect(within(seccao).queryByRole('button', { name: /Associar cartão/ })).not.toBeInTheDocument()
  })

  it('associa um cartão já registado, lido pelo leitor, a este cliente', async () => {
    permissoes.lista = new Set(['cartoes.ver', 'cartoes.cliente'])
    const { pedidos } = servidorFalso({
      'GET /api/cartoes/clientes/5/extrato': { ...extrato, cartoes: [], movimentos: [], saldoActual: 0 },
      'GET /api/cartoes/consulta': outroCartao,
      'POST /api/cartoes/9/cliente': { ...outroCartao, clienteId: 5, clienteNome: 'Ana Fiel' },
    })
    abrir()
    expect(await screen.findByText(/ainda não tem cartões/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Associar cartão/ }))
    const dialogo = screen.getByRole('dialog')
    fireEvent.change(within(dialogo).getByLabelText('Cartão'), { target: { value: ';899999999999=2512?' } })
    fireEvent.submit(within(dialogo).getByLabelText('Cartão').closest('form')!)
    expect(await within(dialogo).findByText(/passa a contar como adiantamento de Ana Fiel/)).toBeInTheDocument()
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Associar' }))
    await vi.waitFor(() => expect(pedidos.some((p) => p.metodo === 'POST' && p.url.endsWith('/api/cartoes/9/cliente'))).toBe(true))
    expect(pedidos.find((p) => p.metodo === 'POST')!.corpo).toEqual({ clienteId: 5, rowVersion: 'AQ==' })
  })
})
