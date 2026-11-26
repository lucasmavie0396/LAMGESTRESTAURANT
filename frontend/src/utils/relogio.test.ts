import { describe, expect, it } from 'vitest'
import { avisoRelogio, avisosAbrirDia, dataDoComputador, descreverDiferenca } from './relogio'

// Este computador: 25/11/2026 às 10:00 (hora local).
const agora = new Date(2026, 10, 25, 10, 0, 0)
const servidor = (minutos: number) => new Date(agora.getTime() + minutos * 60_000).toISOString()

describe('relógio do servidor', () => {
  it('só avisa com mais de 10 minutos de diferença, e diz para que lado', () => {
    expect(avisoRelogio(servidor(9), agora.getTime())).toBeNull()
    expect(avisoRelogio(servidor(-10), agora.getTime())).toBeNull()
    expect(avisoRelogio(servidor(5 * 1440), agora.getTime())).toContain('5 dias à frente')
    expect(avisoRelogio(servidor(-200), agora.getTime())).toContain('3 h 20 min atrás')
    expect(descreverDiferenca(45)).toBe('45 min')
  })

  it('abrir o dia com a data certa não pede confirmação', () => {
    expect(dataDoComputador(agora)).toBe('2026-11-25')
    expect(avisosAbrirDia({ data: '2026-11-25', agoraServidorUtc: servidor(0), ultimoDia: '2026-11-24', agora })).toEqual([])
  })

  it('servidor adiantado: avisa da hora, da data deste computador e dos dias saltados', () => {
    const avisos = avisosAbrirDia({ data: '2026-11-30', agoraServidorUtc: servidor(5 * 1440), ultimoDia: '2026-11-24', agora })
    expect(avisos).toHaveLength(3)
    expect(avisos[0]).toContain('à frente')
    expect(avisos[1]).toContain('não é a data deste computador')
    expect(avisos[2]).toContain('vai abrir 6 dias depois')
  })

  it('servidor e posto no mesmo computador adiantado: o salto desde o último dia continua a avisar', () => {
    const noFuturo = new Date(2026, 10, 30, 10, 0, 0)
    const avisos = avisosAbrirDia({ data: '2026-11-30', agoraServidorUtc: noFuturo.toISOString(), ultimoDia: '2026-11-24', agora: noFuturo })
    expect(avisos).toEqual([expect.stringContaining('vai abrir 6 dias depois')])
  })
})
