import { afterEach, describe, expect, it } from 'vitest'
import { erroPin } from './validacao'
import { aplicarLoginTactilDoEndereco, guardarLoginTactil, lerLoginTactil } from './terminalLocal'

describe('erroPin (mesmas regras do servidor)', () => {
  it.each(['2580', '90817', '135790'])('aceita %s', (pin) => expect(erroPin(pin)).toBeNull())

  it.each([
    ['12', '4 a 6'],
    ['1234567', '4 a 6'],
    ['12a4', '4 a 6'],
    ['1111', 'iguais'],
    ['1234', 'sequência'],
    ['98765', 'sequência'],
  ])('recusa %s', (pin, motivo) => expect(erroPin(pin)).toContain(motivo))
})

describe('ecrã de entrada táctil do posto', () => {
  afterEach(() => {
    guardarLoginTactil(false)
    window.history.replaceState(null, '', '/')
  })

  it('o atalho com ?tactil=1 liga o modo táctil e retira o parâmetro do endereço', () => {
    window.history.replaceState(null, '', '/login?posto=3&tactil=1')
    aplicarLoginTactilDoEndereco()
    expect(lerLoginTactil()).toBe(true)
    expect(window.location.search).toBe('?posto=3')

    window.history.replaceState(null, '', '/?tactil=0')
    aplicarLoginTactilDoEndereco()
    expect(lerLoginTactil()).toBe(false)
  })
})
