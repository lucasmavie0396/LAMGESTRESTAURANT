import { describe, expect, it } from 'vitest'
import type { GrupoPos } from '../../types/api'
import { alternarOpcao, gruposEmFalta } from './acompanhamentos'

const umSo: GrupoPos = { id: 1, nome: 'Acompanhamento', minimo: 1, maximo: 1, opcoes: [{ id: 10, nome: 'Arroz' }, { id: 11, nome: 'Batata' }] }
const ateDois: GrupoPos = { id: 2, nome: 'Extras', minimo: 0, maximo: 2, opcoes: [{ id: 20, nome: 'Salada' }, { id: 21, nome: 'Ovo' }, { id: 22, nome: 'Feijão' }] }

describe('escolha dos acompanhamentos no POS', () => {
  it('num grupo de uma só escolha, tocar noutra opção troca a escolhida', () => {
    expect(alternarOpcao([10], umSo, 11)).toEqual([11])
    expect(alternarOpcao([10, 20], umSo, 11)).toEqual([20, 11])
  })

  it('tocar na escolhida desmarca-a', () => {
    expect(alternarOpcao([10], umSo, 10)).toEqual([])
  })

  it('não passa do máximo do grupo', () => {
    expect(alternarOpcao([20, 21], ateDois, 22)).toEqual([20, 21])
    expect(alternarOpcao([20], ateDois, 22)).toEqual([20, 22])
  })

  it('indica os grupos sem o mínimo de escolhas', () => {
    expect(gruposEmFalta([umSo, ateDois], [])).toEqual([umSo])
    expect(gruposEmFalta([umSo, ateDois], [11])).toEqual([])
  })
})
