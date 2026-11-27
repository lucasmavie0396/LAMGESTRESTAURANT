import { describe, expect, it } from 'vitest'
import { precoNaTabela } from '../types/catalogo'

describe('preço do produto na tabela de preço', () => {
  const produto = { precoVenda: 100, precos: [{ tabelaPrecoId: 3, preco: 80 }] }

  it('usa o preço da tabela quando o produto o tem', () => {
    expect(precoNaTabela(produto, 3)).toBe(80)
  })

  it('sem tabela, ou sem preço nessa tabela, usa o preço normal', () => {
    expect(precoNaTabela(produto, null)).toBe(100)
    expect(precoNaTabela(produto, 9)).toBe(100)
    expect(precoNaTabela({ precoVenda: 50 }, 3)).toBe(50)
  })

  it('um preço zero na tabela é válido (oferta)', () => {
    expect(precoNaTabela({ precoVenda: 50, precos: [{ tabelaPrecoId: 1, preco: 0 }] }, 1)).toBe(0)
  })
})
