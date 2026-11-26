import { afterEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, screen } from '@testing-library/react'
import { documentoLista, montarLista, obterTodasPaginas, textoCelula } from './exportarLista'
import { ExportarLista } from '../components/ExportarLista'
import { AvisosProvider } from '../contexts/AvisosContext'
import { desenhar, servidorFalso } from '../testes/servidorFalso'

const csv = vi.hoisted(() => ({ descarregar: vi.fn() }))
vi.mock('./csv', () => ({ descarregarCsv: csv.descarregar }))
vi.mock('../contexts/SessaoContext', () => ({ useSessao: () => ({ utilizador: { nomeCompleto: 'Gerente Teste' }, tem: () => true }) }))

describe('exportar listas', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    csv.descarregar.mockClear()
  })

  it('pede todas as páginas com os filtros, 200 de cada vez', async () => {
    const { pedidos } = servidorFalso({
      'GET /api/clientes': (p: { url: string }) => {
        const pagina = Number(new URL(p.url, 'http://x').searchParams.get('pagina'))
        return { itens: Array.from({ length: pagina < 3 ? 200 : 50 }, (_, i) => ({ id: pagina * 1000 + i })), total: 450, numeroPagina: pagina, tamanhoPagina: 200 }
      },
    })
    const { itens, cortada } = await obterTodasPaginas<{ id: number }>('/api/clientes', { pesquisa: 'ana', activo: true })
    expect(itens).toHaveLength(450)
    expect(cortada).toBe(false)
    expect(pedidos.map((p) => p.url)).toEqual([1, 2, 3].map((n) => `/api/clientes?pesquisa=ana&activo=true&pagina=${n}&tamanho=200`))
  })

  it('o PDF leva o título, os filtros, a grelha com o texto à esquerda e os números formatados', () => {
    const lista = montarLista('produtos', 'Produtos', ['Activos', false, null], [
      { titulo: 'Nome', valor: (p: { nome: string; preco: number }) => p.nome },
      { titulo: 'Preço (MT)', valor: (p: { nome: string; preco: number }) => p.preco },
    ], [{ nome: 'Prato', preco: 450.5 }, { nome: 'Água', preco: 50 }])
    expect(lista.filtros).toEqual(['Activos'])
    const doc = documentoLista(lista, 'Restaurante Teste', 'Gerente Teste', new Date('2026-11-26T10:00:00Z'))
    expect(doc.cabecalho?.[0].texto).toBe('Restaurante Teste')
    expect(doc.linhas[0]).toMatchObject({ texto: 'PRODUTOS', direita: '2 registos', titulo: true })
    expect(doc.linhas.some((l) => l.texto === 'Filtros: Activos')).toBe(true)
    const cabecalho = doc.linhas.find((l) => l.cabecalho)!
    expect(cabecalho).toMatchObject({ celulas: ['Nome', 'Preço (MT)'], colunasTexto: 1 })
    expect(doc.linhas.filter((l) => l.celulas && !l.cabecalho).map((l) => l.celulas)).toEqual([['Prato', '450,50'], ['Água', '50']])
    expect(textoCelula(null)).toBe('—')
    expect(textoCelula(1.234)).toBe('1,234')
  })

  it('o botão regista a exportação na auditoria e descarrega o ficheiro', async () => {
    const { pedidos } = servidorFalso({ 'POST /api/exportacoes': null })
    desenhar(<AvisosProvider><ExportarLista obter={async () => montarLista('clientes', 'Clientes', ['Activos'], [{ titulo: 'Nome', valor: (x: { n: string }) => x.n }], [{ n: 'Ana' }])} /></AvisosProvider>)
    fireEvent.click(screen.getByRole('button', { name: /Exportar/ }))
    fireEvent.click(screen.getByRole('menuitem', { name: /CSV/ }))
    await vi.waitFor(() => expect(csv.descarregar).toHaveBeenCalledWith('clientes', ['Nome'], [['Ana']]))
    expect(pedidos.find((p) => p.url === '/api/exportacoes')?.corpo).toEqual({ lista: 'Clientes', formato: 'csv', linhas: 1, filtros: 'Activos' })
  })
})
