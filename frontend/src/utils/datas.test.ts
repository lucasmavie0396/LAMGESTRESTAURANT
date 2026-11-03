import { describe, expect, it } from 'vitest'
import { localParaUtc, somarDias, utcParaLocal } from './datas'

describe('datas (Maputo, UTC+2)', () => {
  it('converte hora local para UTC e de volta', () => {
    expect(localParaUtc('2026-11-03', '19:00')).toBe('2026-11-03T17:00:00.000Z')
    expect(utcParaLocal('2026-11-03T23:30:00Z')).toEqual({ data: '2026-11-04', hora: '01:30' })
  })

  it('soma dias atravessando o fim do mês', () => {
    expect(somarDias('2026-11-30', 1)).toBe('2026-12-01')
  })
})
