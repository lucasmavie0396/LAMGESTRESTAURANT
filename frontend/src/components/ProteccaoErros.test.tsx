import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { ProteccaoErros } from './ProteccaoErros'
import { reportarErro } from '../api/errosEcra'

function Avaria(): never {
  throw new Error('falha de teste no componente')
}

describe('protecção contra erros de ecrã', () => {
  let envios: { url: string; corpo: Record<string, unknown> }[]

  beforeEach(() => {
    envios = []
    vi.stubGlobal('fetch', vi.fn((url: string, init: RequestInit) => {
      envios.push({ url, corpo: JSON.parse(String(init.body)) })
      return Promise.resolve(new Response(null, { status: 204 }))
    }))
    // O React escreve o erro na consola: aqui é esperado.
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('um erro num componente mostra a mensagem com «Recarregar» e é enviado para o servidor', () => {
    render(<ProteccaoErros origem="página"><Avaria /></ProteccaoErros>)

    expect(screen.getByRole('alert')).toHaveTextContent('Ocorreu um erro neste ecrã')
    expect(screen.getByRole('button', { name: /Recarregar o ecrã/ })).toBeInTheDocument()
    const envio = envios.find((e) => String(e.corpo.mensagem).includes('falha de teste no componente'))
    expect(envio?.url).toBe('/api/sistema/erros-cliente')
    expect(envio?.corpo.origem).toBe('página')
    expect(String(envio?.corpo.pilha)).toContain('Componentes:')
  })

  it('sem erro mostra o conteúdo normal', () => {
    render(<ProteccaoErros origem="página"><p>Conteúdo</p></ProteccaoErros>)
    expect(screen.getByText('Conteúdo')).toBeInTheDocument()
    expect(envios).toHaveLength(0)
  })

  it('não repete o mesmo erro nem envia ruído conhecido do navegador', () => {
    reportarErro(new Error('repetido'), 'janela')
    reportarErro(new Error('repetido'), 'janela')
    reportarErro('ResizeObserver loop completed with undelivered notifications.', 'janela')
    expect(envios.filter((e) => String(e.corpo.mensagem).includes('repetido'))).toHaveLength(1)
    expect(envios.some((e) => String(e.corpo.mensagem).includes('ResizeObserver'))).toBe(false)
  })
})
