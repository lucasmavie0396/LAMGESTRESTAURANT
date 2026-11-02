import { describe, expect, it } from 'vitest'
import { gerarCsv } from './csv'

describe('gerarCsv', () => {
  it('usa ponto e vírgula, vírgula decimal e escapa aspas e separadores', () => {
    const csv = gerarCsv(['Produto', 'Total'], [['Frango; grelhado', 450.5], ['Prato "especial"', 10], [null, 0]])
    expect(csv).toBe('Produto;Total\r\n"Frango; grelhado";450,5\r\n"Prato ""especial""";10\r\n;0')
  })
})
