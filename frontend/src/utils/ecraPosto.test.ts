import { aplicarEcraPosto, definirEcraPosto, descreverEcra, lerEcraPosto } from './ecraPosto'

describe('ecrã do posto', () => {
  beforeEach(() => {
    localStorage.clear()
    aplicarEcraPosto({ tactil: false, escala: 100 })
  })

  it('por omissão é o ecrã normal', () => {
    expect(lerEcraPosto()).toEqual({ tactil: false, escala: 100 })
    expect(document.documentElement.dataset.tactil).toBeUndefined()
    expect(document.documentElement.style.fontSize).toBe('')
  })

  it('guarda e aplica o modo táctil e o tamanho', () => {
    definirEcraPosto({ tactil: true, escala: 110 })
    expect(lerEcraPosto()).toEqual({ tactil: true, escala: 110 })
    expect(document.documentElement.dataset.tactil).toBe('1')
    expect(document.documentElement.style.fontSize).toBe('110%')
    expect(descreverEcra(lerEcraPosto())).toBe('modo táctil · tamanho 110%')
  })

  it('ignora valores guardados inválidos', () => {
    localStorage.setItem('lamgest.ecraPosto', JSON.stringify({ tactil: 'sim', escala: 300 }))
    expect(lerEcraPosto()).toEqual({ tactil: false, escala: 100 })
    localStorage.setItem('lamgest.ecraPosto', '{')
    expect(lerEcraPosto()).toEqual({ tactil: false, escala: 100 })
  })
})
