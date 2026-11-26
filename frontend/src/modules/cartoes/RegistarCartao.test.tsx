import { afterEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { act, cleanup, fireEvent, screen, within } from '@testing-library/react'
import { RegistarCartao } from './PaginaCartoes'
import { AvisosProvider } from '../../contexts/AvisosContext'
import { desenhar, RespostaHttp, servidorFalso } from '../../testes/servidorFalso'
import type { PontePda } from '../../utils/pda'

/** App Android falsa: só o que a leitura de cartões usa. */
function appFalsa(nfc: '' | 'desligado' | 'sem-nfc' = '') {
  const ponte = {
    terminalId: () => 0, posto: () => '', destino: () => 'posto', definirDestino: () => {}, temImpressora: () => false, colunas: () => 32,
    imprimir: () => '', configurar: () => {},
    lerCodigo: vi.fn(), temNfc: () => nfc !== 'sem-nfc', lerNfc: vi.fn(() => nfc), pararNfc: vi.fn(), definicoesNfc: vi.fn(),
  } satisfies PontePda
  window.LamgestPDA = ponte
  return ponte
}

const enviar = (evento: string, texto: string) => act(async () => { window.dispatchEvent(new CustomEvent(evento, { detail: texto })) })

const registado = { id: 7, numero: '04A1B2C3D4E580', titular: 'Ana', saldo: 350, activo: true, criadoEmUtc: '2026-11-20T10:00:00Z', ultimoMovimentoUtc: null, rowVersion: 'AA==' }

function abrir() {
  const aoAbrir = vi.fn()
  desenhar(<AvisosProvider><RegistarCartao aoFechar={() => {}} aoRegistar={() => {}} aoAbrir={aoAbrir} /></AvisosProvider>)
  return aoAbrir
}

describe('registar cartão evento com o telemóvel', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    delete window.LamgestPDA
  })

  it('sem a app (computador) não mostra os botões da câmara nem do NFC', () => {
    servidorFalso({ 'GET /api/caixa/metodos-pagamento': [] })
    abrir()
    expect(screen.queryByRole('button', { name: /câmara/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /NFC/ })).not.toBeInTheDocument()
  })

  it('lê o chip por NFC, preenche o número e, se o cartão já existe, mostra-o em vez de registar outra vez', async () => {
    const ponte = appFalsa()
    servidorFalso({ 'GET /api/caixa/metodos-pagamento': [], 'GET /api/cartoes/consulta': registado })
    const aoAbrir = abrir()

    fireEvent.click(screen.getByRole('button', { name: 'Ler o cartão sem contacto (NFC)' }))
    expect(screen.getByText('Encoste o cartão às costas do telemóvel')).toBeInTheDocument()
    expect(ponte.lerNfc).toHaveBeenCalledOnce()

    await enviar('lamgest-nfc', '04A1B2C3D4E580')
    expect(screen.getByLabelText('Número do cartão')).toHaveValue('04A1B2C3D4E580')
    expect(await screen.findByText('Este cartão já está registado')).toBeInTheDocument()
    expect(screen.queryByText('Encoste o cartão às costas do telemóvel')).not.toBeInTheDocument()
    expect(ponte.pararNfc).toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Registar' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: /Abrir cartão/ }))
    expect(aoAbrir).toHaveBeenCalledWith(registado)
  })

  it('lê o código com a câmara; cartão novo pode ser registado', async () => {
    const ponte = appFalsa()
    const { pedidos } = servidorFalso({ 'GET /api/caixa/metodos-pagamento': [], 'GET /api/cartoes/consulta': new RespostaHttp(404, { title: 'Não encontrado' }) })
    abrir()

    fireEvent.click(screen.getByRole('button', { name: 'Ler o código do cartão com a câmara' }))
    expect(ponte.lerCodigo).toHaveBeenCalledOnce()
    await enviar('lamgest-codigo', ' 123456789012 ')

    expect(await screen.findByDisplayValue('123456789012')).toBeInTheDocument()
    await vi.waitFor(() => expect(pedidos.some((p) => p.url.includes('/api/cartoes/consulta?numero=123456789012'))).toBe(true))
    expect(screen.queryByText('Este cartão já está registado')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Registar' })).toBeEnabled()
  })

  it('com o NFC desligado pede para o ligar nas definições', () => {
    const ponte = appFalsa('desligado')
    servidorFalso({ 'GET /api/caixa/metodos-pagamento': [] })
    abrir()

    fireEvent.click(screen.getByRole('button', { name: 'Ler o cartão sem contacto (NFC)' }))
    expect(screen.getByText(/O NFC deste telemóvel está desligado/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Abrir definições do NFC/ }))
    expect(ponte.definicoesNfc).toHaveBeenCalledOnce()
  })

  it('uma leitura cancelada não apanha o cartão seguinte', async () => {
    appFalsa()
    servidorFalso({ 'GET /api/caixa/metodos-pagamento': [], 'GET /api/cartoes/consulta': new RespostaHttp(404) })
    abrir()

    fireEvent.click(screen.getByRole('button', { name: 'Ler o cartão sem contacto (NFC)' }))
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Ler cartão sem contacto' })).getByRole('button', { name: 'Cancelar' }))
    expect(screen.queryByText('Encoste o cartão às costas do telemóvel')).not.toBeInTheDocument()
    await enviar('lamgest-nfc', 'AABBCCDD')
    expect(screen.getByLabelText('Número do cartão')).toHaveValue('')
  })
})
