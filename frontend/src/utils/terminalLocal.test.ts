import { describe, expect, it } from 'vitest'
import { numeroPostoDoEndereco } from './terminalLocal'

describe('numeroPostoDoEndereco', () => {
  it('lê o número do posto do atalho', () => {
    expect(numeroPostoDoEndereco('?posto=3')).toBe(3)
    expect(numeroPostoDoEndereco('?x=1&posto=120')).toBe(120)
  })

  it('ignora valores inválidos', () => {
    expect(numeroPostoDoEndereco('')).toBeNull()
    expect(numeroPostoDoEndereco('?posto=0')).toBeNull()
    expect(numeroPostoDoEndereco('?posto=1000')).toBeNull()
    expect(numeroPostoDoEndereco('?posto=2a')).toBeNull()
    expect(numeroPostoDoEndereco('?posto=-1')).toBeNull()
  })
})
