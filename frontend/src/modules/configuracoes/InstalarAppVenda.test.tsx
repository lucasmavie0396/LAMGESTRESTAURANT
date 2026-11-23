import { afterEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { InstalarAppVenda } from './Postos'
import { desenhar, servidorFalso } from '../../testes/servidorFalso'

const URL1 = 'http://192.168.1.10:8080/api/app-venda/LAMGest-Venda.apk'
const URL2 = 'http://10.0.0.5:8080/api/app-venda/LAMGest-Venda.apk'

describe('instalar a app de venda pelo servidor', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('com a APK no servidor mostra o QR do endereço de descarga e muda-o ao escolher outra placa', async () => {
    const { pedidos } = servidorFalso({
      'GET /api/configuracoes/postos/app-venda': { disponivel: true, tamanho: 2_236_997, actualizadaEmUtc: '2026-11-23T13:17:51Z', enderecos: [URL1, URL2] },
      'POST /api/configuracoes/postos/qr': { svg: '<svg xmlns="http://www.w3.org/2000/svg"/>' },
    })
    desenhar(<InstalarAppVenda />)

    expect(await screen.findByAltText('QR code para descarregar a app de venda')).toBeInTheDocument()
    expect(screen.getByText(/^Ficheiro de 2\.1 MB, versão de/)).toBeInTheDocument()
    const qrs = () => pedidos.filter((p) => p.metodo === 'POST').map((p) => (p.corpo as { texto: string }).texto)
    expect(qrs()).toEqual([URL1])

    fireEvent.change(screen.getByLabelText('Endereço de descarga'), { target: { value: URL2 } })
    await waitFor(() => expect(qrs()).toEqual([URL1, URL2]))
  })

  it('sem a APK no servidor pede para actualizar com o Setup e não gera QR', async () => {
    const { pedidos } = servidorFalso({
      'GET /api/configuracoes/postos/app-venda': { disponivel: false, tamanho: null, actualizadaEmUtc: null, enderecos: [URL1] },
    })
    desenhar(<InstalarAppVenda />)

    expect(await screen.findByText(/ainda não tem a app de venda/)).toBeInTheDocument()
    expect(screen.queryByAltText('QR code para descarregar a app de venda')).not.toBeInTheDocument()
    expect(pedidos.some((p) => p.metodo === 'POST')).toBe(false)
  })
})
