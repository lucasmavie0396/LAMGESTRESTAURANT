import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { useLeitorCodigos } from './useLeitorCodigos'
import { RespostaHttp, servidorFalso } from '../../testes/servidorFalso'
import type { Produto } from '../../types/api'

const sumo = { id: 5, codigo: 'SUMO', codigoBarras: '7891234567890', nome: 'Sumo' } as unknown as Produto
const queijo = { id: 9, codigo: 'QUEIJO', codigoBarras: null, nome: 'Queijo' } as unknown as Produto

/** O leitor escreve as teclas muito depressa e termina com Enter. */
function passarNoLeitor(codigo: string, alvo: EventTarget = document.body) {
  let t = 1000
  for (const key of [...codigo, 'Enter']) {
    const e = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
    Object.defineProperty(e, 'timeStamp', { value: (t += 10) })
    alvo.dispatchEvent(e)
  }
}

function servidor() {
  return servidorFalso({
    'GET /api/produtos*': (p: { url: string }) => {
      const pesquisa = new URL(p.url, 'http://x').searchParams.get('pesquisa')
      return { itens: pesquisa === sumo.codigoBarras ? [sumo] : [], total: 0, numeroPagina: 1, tamanhoPagina: 20 }
    },
    'GET /api/balanca/etiqueta/2000009012507': { produto: queijo, quantidade: 1.25 },
    'GET /api/balanca/etiqueta/*': new RespostaHttp(404),
  })
}

describe('leitor de código de barras do POS', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  function iniciar(opcoes: Partial<Parameters<typeof useLeitorCodigos>[0]> = {}) {
    const aoEncontrar = vi.fn()
    const aoErro = vi.fn()
    const aoLimparPesquisa = vi.fn()
    const h = renderHook((p: Parameters<typeof useLeitorCodigos>[0]) => useLeitorCodigos(p), {
      initialProps: { activo: true, ocupado: false, comDialogo: false, aoEncontrar, aoErro, aoLimparPesquisa, ...opcoes },
    })
    return { ...h, aoEncontrar, aoErro, aoLimparPesquisa }
  }

  it('código lido fora dos campos lança o produto; o sinal «a escutar» fica no body', async () => {
    servidor()
    const { aoEncontrar } = iniciar()
    expect(document.body.dataset.leitorCodigos).toBe('activo')
    act(() => passarNoLeitor('7891234567890'))
    await waitFor(() => expect(aoEncontrar).toHaveBeenCalledWith(sumo))
  })

  it('duas leituras seguidas não se perdem: ficam em fila', async () => {
    servidor()
    const { aoEncontrar } = iniciar()
    act(() => {
      passarNoLeitor('7891234567890')
      passarNoLeitor('7891234567890')
    })
    await waitFor(() => expect(aoEncontrar).toHaveBeenCalledTimes(2))
  })

  it('etiqueta da balança (EAN-13 com peso) lança o produto com a quantidade', async () => {
    servidor()
    const { aoEncontrar } = iniciar()
    act(() => passarNoLeitor('2000009012507'))
    await waitFor(() => expect(aoEncontrar).toHaveBeenCalledWith(queijo, 1.25))
  })

  it('código desconhecido avisa o operador', async () => {
    servidor()
    const { aoErro } = iniciar()
    act(() => passarNoLeitor('ABC999'))
    await waitFor(() => expect(aoErro).toHaveBeenCalledWith('Código ABC999 não encontrado.'))
  })

  it('a escrever num campo de texto ou com um diálogo aberto, as teclas não são do leitor', async () => {
    servidor()
    const campo = document.createElement('input')
    document.body.appendChild(campo)
    const { aoEncontrar, rerender, aoErro, aoLimparPesquisa } = iniciar()
    act(() => passarNoLeitor('7891234567890', campo))
    rerender({ activo: true, ocupado: true, comDialogo: true, aoEncontrar, aoErro, aoLimparPesquisa })
    expect(document.body.dataset.leitorCodigos).toBeUndefined()
    act(() => passarNoLeitor('7891234567890'))
    await new Promise((r) => setTimeout(r, 50))
    expect(aoEncontrar).not.toHaveBeenCalled()
    campo.remove()
  })

  it('a gravar, o leitor continua a escutar e trata o código depois', async () => {
    servidor()
    const { aoEncontrar, rerender, aoErro, aoLimparPesquisa } = iniciar({ ocupado: true })
    act(() => passarNoLeitor('7891234567890'))
    await new Promise((r) => setTimeout(r, 50))
    expect(aoEncontrar).not.toHaveBeenCalled()
    rerender({ activo: true, ocupado: false, comDialogo: false, aoEncontrar, aoErro, aoLimparPesquisa })
    await waitFor(() => expect(aoEncontrar).toHaveBeenCalledWith(sumo))
  })

  it('código vindo da pesquisa limpa o campo quando encontra o produto', async () => {
    servidor()
    const { result, aoEncontrar, aoLimparPesquisa } = iniciar()
    act(() => result.current('7891234567890', true))
    await waitFor(() => expect(aoEncontrar).toHaveBeenCalledWith(sumo))
    expect(aoLimparPesquisa).toHaveBeenCalled()
  })
})
