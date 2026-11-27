import { describe, expect, it } from 'vitest'
import { hojeComercialIso } from './datas'

describe('dia comercial', () => {
  // 27/11/2026 às 02:30 em Maputo (UTC+2) = 00:30 UTC.
  const madrugada = new Date('2026-11-27T00:30:00Z')

  it('sem hora de fim (meia-noite) é o dia de calendário', () => {
    expect(hojeComercialIso(0, madrugada)).toBe('2026-11-27')
  })

  it('com o dia a terminar às 06:00, às 02:30 ainda é o dia anterior; às 06:00 já é o novo dia', () => {
    expect(hojeComercialIso(6, madrugada)).toBe('2026-11-26')
    expect(hojeComercialIso(6, new Date('2026-11-27T04:00:00Z'))).toBe('2026-11-27')
  })
})
