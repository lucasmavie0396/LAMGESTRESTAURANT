import { describe, expect, it } from 'vitest'
import type { DocumentoImpressao, LinhaImpressao } from '../types/api'
import { htmlDocumento } from './impressaoDocumento'

const linha = (texto: string, direita: string | null = null): LinhaImpressao => ({ texto, direita, alinhamento: 'Esquerda', negrito: false, grande: false, separador: false })
const tabela = (celulas: string[], cabecalho = false): LinhaImpressao => ({ ...linha(celulas[0]), celulas, cabecalho, negrito: cabecalho })

describe('documentos impressos pelo navegador', () => {
  const recibo: DocumentoImpressao = {
    titulo: 'Recibo',
    cabecalho: [{ ...linha('Restaurante Sol'), negrito: true, grande: true }, linha('NUIT 400123456')],
    linhas: [
      tabela(['Descrição', 'Qtd.', 'Preço', 'Total (MT)'], true),
      tabela(['Frango grelhado', '2', '450,50', '901,00']),
      linha('TOTAL', '901,00 MT'), // linhas de tabela seguidas formam uma tabela: esta separa as duas
      tabela(['Resumo do IVA', 'Base', 'IVA'], true),
      tabela(['IVA 16 %', '776,72', '124,28']),
      linha('<script>'),
    ],
  }

  it('gera o papel pedido, escapa o texto e põe o logótipo em todos os papéis', () => {
    const talao = htmlDocumento(recibo, 80, '/api/imagens/abc')
    expect(talao).toContain('size: 80mm auto')
    expect(talao).toContain('&lt;script&gt;')
    expect(talao).toContain('<span class="v">901,00 MT</span>')
    for (const papel of [58, 80, 210] as const) expect(htmlDocumento(recibo, papel, '/api/imagens/abc')).toContain('<img class="logo" src="/api/imagens/abc"')
    expect(htmlDocumento(recibo, 80, null)).not.toContain('<img')
    expect(htmlDocumento(recibo, 210, '/x')).toContain('size: A4')
  })

  it('repete o documento pelo número de cópias, cada uma na sua página', () => {
    const contar = (html: string) => html.split('<section class="copia">').length - 1
    expect(contar(htmlDocumento(recibo, 80, null))).toBe(1)
    expect(contar(htmlDocumento(recibo, 80, null, 3))).toBe(3)
    expect(htmlDocumento(recibo, 80, null, 3)).toContain('.copia + .copia { break-before: page; }')
  })

  it('identificação do estabelecimento ao lado do logótipo em A4 e centrada nos talões', () => {
    expect(htmlDocumento(recibo, 210, '/x')).toContain('<header class="lado">')
    expect(htmlDocumento(recibo, 80, '/x')).toContain('<header class="">')
    expect(htmlDocumento(recibo, 80, '/x')).toContain('NUIT 400123456')
  })

  it('tabelas: em A4 uma linha por registo; nos talões a tabela de artigos empilha e a de 3 colunas não', () => {
    const a4 = htmlDocumento(recibo, 210, null)
    expect(a4).toContain('<tr class=""><td class="desc">Frango grelhado</td><td class="v">2</td><td class="v">450,50</td><td class="v">901,00</td></tr>')
    const talao = htmlDocumento(recibo, 80, null)
    expect(talao).toContain('<table class="emp"><tr class="th"><td class="desc" colspan="3">Descrição</td></tr><tr class="th valores">')
    expect(talao).toContain('<table class=""><tr class="th"><td class="desc">Resumo do IVA</td>')
  })
})
