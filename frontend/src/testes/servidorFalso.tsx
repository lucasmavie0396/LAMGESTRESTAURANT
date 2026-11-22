// Servidor falso para os testes de componentes: responde aos pedidos (fetch) com dados fixos e regista o que o ecrã enviou.
import { render, type RenderResult } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { LigacaoProvider } from '../contexts/LigacaoContext'

export interface PedidoRecebido {
  metodo: string
  url: string
  corpo: unknown
  cabecalhos: Record<string, string>
}

type Resposta = unknown | ((p: PedidoRecebido) => unknown)
/** «GET /api/...» ou «POST /api/...»: o caminho pode terminar em «*» (prefixo). Resposta: JSON (200) ou { estado, corpo }. */
export type Rotas = Record<string, Resposta>

export class RespostaHttp {
  readonly estado: number
  readonly corpo?: unknown
  constructor(estado: number, corpo?: unknown) {
    this.estado = estado
    this.corpo = corpo
  }
}

/** Substitui o fetch: cada pedido procura a sua rota; sem rota, responde 404 (e o teste vê-o em `pedidos`). */
export function servidorFalso(rotas: Rotas) {
  const pedidos: PedidoRecebido[] = []
  const todas: Rotas = {
    'GET /api/saude': { api: true, baseDados: true, migracoesPendentes: false, horaServidorUtc: new Date().toISOString() },
    'GET /api/licenca/estado': { bloqueada: false },
    ...rotas,
  }
  vi.stubGlobal('fetch', vi.fn(async (entrada: string | URL, init?: RequestInit) => {
    const url = String(entrada)
    const metodo = (init?.method ?? 'GET').toUpperCase()
    const cabecalhos = Object.fromEntries(Object.entries((init?.headers ?? {}) as Record<string, string>))
    const corpo = typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body
    const pedido = { metodo, url, corpo, cabecalhos }
    if (!url.endsWith('/api/saude') && !url.endsWith('/api/licenca/estado')) pedidos.push(pedido)
    const caminho = url.split('?')[0]
    const chave = Object.keys(todas).find((k) => {
      const [m, p] = k.split(' ')
      return m === metodo && (p.endsWith('*') ? url.startsWith(p.slice(0, -1)) : p === caminho)
    })
    if (!chave) return new Response(JSON.stringify({ title: 'Não encontrado' }), { status: 404 })
    const valor = todas[chave]
    const r = typeof valor === 'function' ? (valor as (p: PedidoRecebido) => unknown)(pedido) : valor
    if (r instanceof RespostaHttp)
      return new Response(r.corpo === undefined ? null : JSON.stringify(r.corpo), { status: r.estado })
    return new Response(JSON.stringify(r), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }))
  return { pedidos }
}

/** Desenha o componente como na aplicação: com o encaminhamento e o estado da ligação ao servidor. */
export function desenhar(ui: ReactElement, rota = '/'): RenderResult {
  const Envolver = ({ children }: { children: ReactNode }) => (
    <MemoryRouter initialEntries={[rota]}>
      <LigacaoProvider>{children}</LigacaoProvider>
    </MemoryRouter>
  )
  return render(ui, { wrapper: Envolver })
}
