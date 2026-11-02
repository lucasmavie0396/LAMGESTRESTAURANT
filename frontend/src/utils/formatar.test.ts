import { describe, expect, it } from 'vitest'
import { formatarDataHora, formatarMoeda } from './formatar'

// Normaliza separadores de milhares (o Intl pode usar espaço normal ou espaço fino inseparável).
const normalizar = (s: string) => s.replace(/[  ]/g, ' ')

describe('formatarMoeda', () => {
  it('usa duas casas decimais, vírgula decimal e o símbolo MT', () => {
    // Norma CLDR pt-MZ: separador de milhares só a partir de 5 algarismos.
    expect(normalizar(formatarMoeda(12345.5))).toBe('12 345,50 MT')
    expect(formatarMoeda(0.1 + 0.2)).toBe('0,30 MT')
  })

  it('respeita um símbolo configurado', () => {
    expect(formatarMoeda(10, { moeda: 'USD', simboloMoeda: 'US$', fusoHorario: 'UTC' })).toBe('10,00 US$')
  })
})

describe('formatarDataHora', () => {
  it('converte UTC para a hora de Maputo (UTC+2)', () => {
    expect(formatarDataHora('2026-11-02T22:30:00Z')).toContain('00:30')
  })
})
