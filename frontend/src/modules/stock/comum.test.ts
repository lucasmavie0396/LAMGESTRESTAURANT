import { describe, expect, it } from 'vitest'
import type { Artigo } from '../../types/api'
import { agruparArtigos } from './comum'

const artigo = (id: number, nome: string, categoria: string | null) => ({ id, nome, categoria, unidade: 'kg' }) as Artigo

describe('artigos agrupados por categoria', () => {
  it('categorias por ordem alfabética, artigos ordenados e os sem categoria no fim', () => {
    const grupos = agruparArtigos([artigo(1, 'Tomate', 'Legumes'), artigo(2, 'Sal', null), artigo(3, 'Picanha', 'Carnes'), artigo(4, 'Alface', 'Legumes')])
    expect(grupos.map(([g, l]) => [g, l.map((a) => a.nome)])).toEqual([
      ['Carnes', ['Picanha']],
      ['Legumes', ['Alface', 'Tomate']],
      ['Sem categoria', ['Sal']],
    ])
  })
})
