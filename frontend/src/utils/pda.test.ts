import { describe, expect, it } from 'vitest'
import type { DocumentoImpressao, LinhaImpressao } from '../types/api'
import { enderecoPreferido, linhasPda, rotaPermitidaNoPda } from './pda'

const linha = (texto: string, resto: Partial<LinhaImpressao> = {}): LinhaImpressao =>
  ({ texto, direita: null, alinhamento: 'Esquerda', negrito: false, grande: false, separador: false, ...resto })
const doc = (...linhas: LinhaImpressao[]): DocumentoImpressao => ({ titulo: 'T', linhas })

describe('linhasPda', () => {
  it('encosta o valor à direita na largura do papel', () => {
    expect(linhasPda(doc(linha('Total', { direita: '100,00 MT', negrito: true })), 32)).toEqual([
      { t: 'Total' + ' '.repeat(18) + '100,00 MT', a: 0, n: true, g: false },
    ])
  })

  it('passa o valor para a linha seguinte quando não cabe', () => {
    const r = linhasPda(doc(linha('Descrição bastante comprida do produto', { direita: '1 000,00 MT' })), 32)
    expect(r.map((x) => x.t)).toEqual(['Descrição bastante comprida do', 'produto', '1 000,00 MT'])
    expect(r[2].a).toBe(2)
  })

  it('letra grande usa menos colunas e o separador ocupa a largura', () => {
    const r = linhasPda(doc(linha('TOTAL', { direita: '50,00', grande: true }), linha('', { separador: true })), 32)
    expect(r[0].t).toHaveLength(24)
    expect(r[1].t).toBe('-'.repeat(32))
  })

  it('centra e parte palavras maiores do que o papel', () => {
    expect(linhasPda(doc(linha('Centro', { alinhamento: 'Centro' })), 32)[0].a).toBe(1)
    expect(linhasPda(doc(linha('x'.repeat(40))), 32).map((l) => l.t)).toEqual(['x'.repeat(32), 'x'.repeat(8)])
  })

  it('tabela: descrição numa linha e valores alinhados por baixo', () => {
    const r = linhasPda(doc(
      linha('', { celulas: ['Descrição', 'Qtd.', 'Preço', 'IVA', 'Total'], cabecalho: true }),
      linha('', { celulas: ['Frango', '2', '450,50', '16 %', '901,00'] }),
    ), 32)
    expect(r.map((x) => x.t)).toEqual([
      'Descrição',
      'Qtd.   Preço   IVA   Total'.padStart(32),
      'Frango',
      '   2  450,50  16 %  901,00'.padStart(32),
    ])
    expect(r[1].n).toBe(true)
    expect(r[3].n).toBe(false)
  })

  it('inclui o cabeçalho do estabelecimento', () => {
    const r = linhasPda({ titulo: 'T', cabecalho: [linha('Restaurante', { alinhamento: 'Centro' })], linhas: [linha('A')] }, 48)
    expect(r.map((x) => x.t)).toEqual(['Restaurante', 'A'])
  })
})

describe('rotaPermitidaNoPda', () => {
  it('só deixa as áreas de venda', () => {
    expect(rotaPermitidaNoPda('/pos')).toBe(true)
    expect(rotaPermitidaNoPda('/caixa')).toBe(true)
    expect(rotaPermitidaNoPda('/relatorios')).toBe(false)
    expect(rotaPermitidaNoPda('/posicoes')).toBe(false)
  })
})

describe('enderecoPreferido', () => {
  const enderecos = ['http://10.0.0.5:8080', 'http://192.168.1.10:8080']
  it('escolhe pelo IP do posto', () => {
    expect(enderecoPreferido(enderecos, '192.168.1.10')).toBe(enderecos[1])
    expect(enderecoPreferido(enderecos, '192.168.1.44')).toBe(enderecos[1])
  })
  it('sem posto conhecido usa o primeiro; sem rede não há endereço', () => {
    expect(enderecoPreferido(enderecos, null)).toBe(enderecos[0])
    expect(enderecoPreferido(enderecos, '172.16.0.9')).toBe(enderecos[0])
    expect(enderecoPreferido([], '192.168.1.1')).toBeNull()
  })
})
