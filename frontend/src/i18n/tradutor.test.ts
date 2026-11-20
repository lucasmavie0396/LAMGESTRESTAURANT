import { afterEach, describe, expect, it } from 'vitest'
import { Tradutor } from './tradutor'
import en from './en.json'

const dic = {
  textos: { Guardar: 'Save', Mesa: 'Table', 'Fechar caixa': 'Close register', Zona: 'Area' },
  modelos: { 'Pedido {0} estornado.': 'Order {0} reversed.', '{0} criada.': '{0} created.' },
}

describe('Tradutor', () => {
  let tradutor: Tradutor | null = null
  afterEach(() => {
    tradutor?.parar()
    document.body.innerHTML = ''
  })

  it('traduz texto exacto, mantém os espaços e deixa o desconhecido em Português', () => {
    const t = new Tradutor(dic)
    expect(t.traduzir('Guardar')).toBe('Save')
    expect(t.traduzir('  Mesa ')).toBe('  Table ')
    expect(t.traduzir('Fechar\n   caixa')).toBe('Close register')
    expect(t.traduzir('Frango grelhado')).toBe('Frango grelhado')
    expect(t.traduzir('12,50 MT')).toBe('12,50 MT')
  })

  it('traduz modelos com partes variáveis, e as partes também quando estão no dicionário', () => {
    const t = new Tradutor(dic)
    expect(t.traduzir('Pedido P-0042 estornado.')).toBe('Order P-0042 reversed.')
    expect(t.traduzir('Zona criada.')).toBe('Area created.')
  })

  it('traduz a página e o que o React muda depois, incluindo atributos, sem tocar em translate="no"', async () => {
    document.body.innerHTML = '<button title="Guardar">Guardar</button><p translate="no">Mesa</p><span id="s">Mesa</span>'
    tradutor = new Tradutor(dic)
    tradutor.observar(document.body)
    const botao = document.querySelector('button')!
    expect(botao.textContent).toBe('Save')
    expect(botao.title).toBe('Save')
    expect(document.querySelector('p')!.textContent).toBe('Mesa')

    // Alteração do texto (como o React faz) e nó novo.
    document.getElementById('s')!.firstChild!.nodeValue = 'Fechar caixa'
    const novo = document.createElement('div')
    novo.textContent = 'Pedido 7 estornado.'
    document.body.appendChild(novo)
    await new Promise((r) => setTimeout(r, 0))
    expect(document.getElementById('s')!.textContent).toBe('Close register')
    expect(novo.textContent).toBe('Order 7 reversed.')
  })

  it('o dicionário inglês tem os textos principais e modelos com os mesmos marcadores', () => {
    expect(en.textos['Entrar']).toBe('Sign in')
    expect(en.textos['Fechar caixa']).toBe('Close register')
    for (const [pt, ing] of Object.entries(en.modelos)) {
      const marcas = (s: string) => (s.match(/\{\d+\}/g) ?? []).sort().join()
      expect(marcas(ing), pt).toBe(marcas(pt))
    }
  })
})
