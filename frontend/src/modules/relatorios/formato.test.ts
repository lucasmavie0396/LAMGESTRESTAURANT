// @vitest-environment node
// (o jsdom tem um Uint8Array de outro «realm», que o fflate não reconhece; no navegador é o mesmo)
import { strFromU8, unzipSync } from 'fflate'
import { describe, expect, it } from 'vitest'
import { coluna, gerarXlsx } from '../../utils/xlsx'
import { intervalo, percentagem } from './formato'

describe('intervalo dos relatórios', () => {
  const p = { desde: '2026-01-01', ate: '2026-01-31' }
  it('esta semana começa à segunda-feira', () => {
    expect(intervalo('semana', p, '2026-11-04')).toEqual({ desde: '2026-11-02', ate: '2026-11-04' }) // quarta
    expect(intervalo('semana', p, '2026-11-08')).toEqual({ desde: '2026-11-02', ate: '2026-11-08' }) // domingo
    expect(intervalo('semana', p, '2026-11-02')).toEqual({ desde: '2026-11-02', ate: '2026-11-02' }) // segunda
  })
  it('este ano, mês anterior (atravessa o ano) e personalizado', () => {
    expect(intervalo('ano', p, '2026-11-04')).toEqual({ desde: '2026-01-01', ate: '2026-11-04' })
    expect(intervalo('mesAnterior', p, '2026-01-15')).toEqual({ desde: '2025-12-01', ate: '2025-12-31' })
    expect(intervalo('mesAnterior', p, '2026-03-10')).toEqual({ desde: '2026-02-01', ate: '2026-02-28' })
    expect(intervalo('personalizado', p, '2026-11-04')).toEqual(p)
  })
})

describe('percentagem', () => {
  it('uma casa decimal e total zero', () => {
    expect(percentagem(1, 3)).toBe('33,3%')
    expect(percentagem(5, 0)).toBe('—')
  })
})

describe('exportação Excel (.xlsx)', () => {
  it('colunas A…Z, AA', () => {
    expect([coluna(0), coluna(25), coluna(26), coluna(701)]).toEqual(['A', 'Z', 'AA', 'ZZ'])
  })

  it('gera um xlsx válido com números como números e texto escapado', () => {
    const ficheiros = unzipSync(gerarXlsx(['Produto', 'Total (MT)'], [['Frango & <batatas>', 450.5], ['Água', 60], [null, 0]], 'Vendas'))
    expect(Object.keys(ficheiros).sort()).toEqual(['[Content_Types].xml', '_rels/.rels', 'xl/_rels/workbook.xml.rels', 'xl/styles.xml', 'xl/workbook.xml', 'xl/worksheets/sheet1.xml'])
    const folha = strFromU8(ficheiros['xl/worksheets/sheet1.xml'])
    expect(folha).toContain('<c r="B2" s="2"><v>450.5</v></c>')
    expect(folha).toContain('Frango &amp; &lt;batatas&gt;')
    expect(folha).toContain('<c r="A1" t="inlineStr" s="1">')
    expect(strFromU8(ficheiros['xl/workbook.xml'])).toContain('name="Vendas"')
  })
})
