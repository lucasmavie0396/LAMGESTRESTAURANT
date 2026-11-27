import { afterEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, screen, within } from '@testing-library/react'
import { PagamentosCompra } from './PagamentosCompra'
import { ContasPagarVista } from '../relatorios/RelatoriosOperacaoVista'
import { AvisosProvider } from '../../contexts/AvisosContext'
import { desenhar, servidorFalso } from '../../testes/servidorFalso'
import type { Compra } from '../../types/api'

vi.mock('../../contexts/SessaoContext', () => ({ useSessao: () => ({ tem: () => true }) }))

const compra = {
  id: 4, fornecedorId: 2, fornecedor: 'Distribuidora Sol', numeroDocumento: 'FT 77', dataDocumento: '2026-11-01', dataVencimento: '2026-11-15', armazemId: 1, armazem: 'Principal',
  estado: 'Confirmada', subtotal: 1000, desconto: 0, impostos: 160, total: 1160, observacoes: null, criadoPor: 'Gerente', confirmadaEmUtc: '2026-11-01T10:00:00Z',
  confirmadaPor: 'Gerente', anuladaEmUtc: null, motivoAnulacao: null, itens: [], rowVersion: 'AA==', pago: 500, emDivida: 660,
} as unknown as Compra

describe('pagamentos a fornecedores e contas a pagar', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('regista um pagamento com o que falta pagar e a chave de idempotência', async () => {
    const aoMudar = vi.fn()
    const { pedidos } = servidorFalso({
      'GET /api/compras/4/pagamentos': [{ id: 1, compraId: 4, dataUtc: '2026-11-20T10:00:00Z', valor: 500, metodo: 'M-Pesa', referencia: 'MP-1', sessaoCaixaId: null, utilizador: 'Gerente', estado: 'Valido', anuladoEmUtc: null, motivoAnulacao: null }],
      'GET /api/caixa/metodos-pagamento': [{ id: 1, codigo: 'DINHEIRO', nome: 'Dinheiro', activo: true }, { id: 9, codigo: 'FACTURA_CREDITO', nome: 'Fatura a Crédito', activo: true }],
      'POST /api/compras/4/pagamentos': { id: 2 },
    })
    desenhar(<AvisosProvider><PagamentosCompra compra={compra} aoMudar={aoMudar} /></AvisosProvider>)
    expect(await screen.findByText(/M-Pesa \(MP-1\)/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Registar pagamento/ }))
    const dialogo = screen.getByRole('dialog')
    expect(within(dialogo).getByLabelText('Valor (MT)')).toHaveValue('660')
    await vi.waitFor(() => expect(within(dialogo).queryByRole('option', { name: 'Fatura a Crédito' })).not.toBeInTheDocument())
    await vi.waitFor(() => expect(within(dialogo).getByRole('option', { name: 'Dinheiro' })).toBeInTheDocument())
    fireEvent.click(within(dialogo).getByRole('button', { name: /Registar/ }))
    await vi.waitFor(() => expect(aoMudar).toHaveBeenCalled())
    const post = pedidos.find((p) => p.metodo === 'POST')!
    expect(post.corpo).toMatchObject({ valor: 660, metodoPagamentoId: 1, sessaoCaixaId: null })
    expect(post.cabecalhos['Idempotency-Key']).toBeTruthy()
  })

  it('o relatório de contas a pagar mostra a dívida por fornecedor e as compras vencidas', async () => {
    servidorFalso({
      'GET /api/relatorios/contas-pagar': {
        desde: '2026-11-01', ate: '2026-11-27', hoje: '2026-11-27', totalEmDivida: 660, totalVencido: 660,
        dividas: [{ fornecedorId: 2, fornecedor: 'Distribuidora Sol', compras: 1, emDivida: 660, naoVencido: 0, ate30: 660, de31a60: 0, de61a90: 0, mais90: 0 }],
        comprasEmAberto: [{ id: 4, fornecedor: 'Distribuidora Sol', documento: 'FT 77', data: '2026-11-01', vencimento: '2026-11-15', total: 1160, pago: 500, emDivida: 660, diasAtraso: 12 }],
        pagoNoPeriodo: 500, pagamentos: [], porMetodo: [],
      },
    })
    desenhar(<ContasPagarVista desde="2026-11-01" ate="2026-11-27" />)
    expect(await screen.findByText('FT 77')).toBeInTheDocument()
    expect(screen.getByText(/\(\+12 d\)/)).toBeInTheDocument()
    expect(screen.getAllByText('Distribuidora Sol').length).toBeGreaterThanOrEqual(2)
  })
})
