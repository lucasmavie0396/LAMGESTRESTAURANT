import { describe, expect, it } from 'vitest'
import { pesoNaUnidade } from './useBalanca'

describe('peso da balança na unidade do produto', () => {
  it('kg com 3 casas decimais, g em gramas e as outras unidades não são a peso', () => {
    expect(pesoNaUnidade(1.2504, 'kg')).toBe(1.25)
    expect(pesoNaUnidade(0.485, 'g')).toBe(485)
    expect(pesoNaUnidade(1, 'un')).toBeNull()
    expect(pesoNaUnidade(1, 'l')).toBeNull()
  })
})
