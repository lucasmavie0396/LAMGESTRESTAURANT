// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { campoComTeclado, campoNumerico, escreverNoCampo } from './teclado'

function campo(valor = '', tipo = 'text') {
  const el = document.createElement('input')
  el.type = tipo
  el.value = valor
  document.body.appendChild(el)
  return el
}

describe('teclado no ecrã', () => {
  it('escreve na posição do cursor e avisa o React com o evento input', () => {
    const el = campo('Frango assado')
    const ouvinte = vi.fn()
    el.addEventListener('input', ouvinte)
    el.setSelectionRange(6, 6)
    escreverNoCampo(el, 'X')
    expect(el.value).toBe('FrangoX assado')
    expect(el.selectionStart).toBe(7)
    expect(ouvinte).toHaveBeenCalledOnce()
  })

  it('apaga a selecção ou o carácter antes do cursor e respeita o maxLength', () => {
    const el = campo('sem batata')
    el.setSelectionRange(0, 4)
    escreverNoCampo(el, '', true)
    expect(el.value).toBe('batata')
    escreverNoCampo(el, '', true) // cursor no início: nada para apagar
    expect(el.value).toBe('batata')
    el.setSelectionRange(6, 6)
    escreverNoCampo(el, '', true)
    expect(el.value).toBe('batat')
    el.maxLength = 5
    escreverNoCampo(el, 'a')
    expect(el.value).toBe('batat')
  })

  it('campos numéricos escrevem no fim e abrem o teclado numérico', () => {
    const el = campo('12', 'number')
    escreverNoCampo(el, '.5')
    expect(el.value).toBe('12.5')
    expect(campoNumerico(el)).toBe(true)
    const valor = campo('')
    valor.inputMode = 'decimal'
    expect(campoNumerico(valor)).toBe(true)
    expect(campoNumerico(campo(''))).toBe(false)
  })

  it('só os campos de escrita recebem o teclado', () => {
    expect(campoComTeclado(campo('', 'search'))).toBe(true)
    expect(campoComTeclado(document.createElement('textarea'))).toBe(true)
    expect(campoComTeclado(campo('', 'checkbox'))).toBe(false)
    expect(campoComTeclado(campo('', 'date'))).toBe(false)
    const soLeitura = campo('')
    soLeitura.readOnly = true
    expect(campoComTeclado(soLeitura)).toBe(false)
    const marcado = campo('')
    marcado.dataset.semTeclado = ''
    expect(campoComTeclado(marcado)).toBe(true) // atributo vazio: o React escreve data-sem-teclado="true"
    marcado.dataset.semTeclado = 'true'
    expect(campoComTeclado(marcado)).toBe(false)
  })
})
