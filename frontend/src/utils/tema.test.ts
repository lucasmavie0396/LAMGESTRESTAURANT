import { afterEach, describe, expect, it } from 'vitest'
import { aplicarTema, definirTema, temaGuardado } from './tema'

describe('tema claro/escuro', () => {
  afterEach(() => {
    localStorage.clear()
    document.documentElement.classList.remove('escuro')
  })

  it('sem escolha guardada segue o sistema (claro se não houver preferência)', () => {
    expect(temaGuardado()).toBe('claro')
  })

  it('a escolha fica guardada e muda a classe da página', () => {
    definirTema('escuro')
    expect(document.documentElement.classList.contains('escuro')).toBe(true)
    expect(temaGuardado()).toBe('escuro')
    document.documentElement.classList.remove('escuro')
    aplicarTema()
    expect(document.documentElement.classList.contains('escuro')).toBe(true)
    definirTema('claro')
    expect(document.documentElement.classList.contains('escuro')).toBe(false)
  })
})
