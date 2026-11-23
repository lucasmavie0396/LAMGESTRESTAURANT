import { afterEach, describe, expect, it } from 'vitest'
import type { ConfiguracoesPublicas, DocumentoImpressao, LinhaImpressao } from '../types/api'
import { htmlDocumento, imprimirNoNavegador } from './impressaoDocumento'

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
    expect(a4).toContain('<tr class=""><td class="t">Frango grelhado</td><td class="v">2</td><td class="v">450,50</td><td class="v">901,00</td></tr>')
    expect(a4).toContain('<thead><tr class="th n">')
    const talao = htmlDocumento(recibo, 80, null)
    expect(talao).toContain('<table class="emp"><tr class="th"><td class="desc" colspan="3">Descrição</td></tr><tr class="th valores">')
    expect(talao).toContain('<table class=""><tr class="th"><td class="desc">Resumo do IVA</td>')
  })

  it('relatórios em grelha: em A4 faixa, quadros, colunas de texto, alertas, subtotal e total; nos talões compactos', () => {
    const t = (celulas: string[], extra: Partial<DocumentoImpressao['linhas'][number]> = {}) =>
      ({ ...linha(celulas[0]), celulas, colunasTexto: 2, ...extra })
    const stock: DocumentoImpressao = {
      titulo: 'Stock',
      linhas: [
        { ...linha('EXISTÊNCIAS EM STOCK', 'Em 18/11/2026'), titulo: true, alinhamento: 'Centro' },
        { ...linha('Abaixo do mínimo', '1'), cartao: true, alerta: true },
        { ...linha('Artigos', '2'), cartao: true },
        { ...linha('Ingredientes', '2 artigos · 5 162,50 MT'), faixa: true, negrito: true },
        t(['Código', 'Artigo', 'Quantidade', 'Valor'], { cabecalho: true }),
        t(['A0003', 'ARROZ', '14,95 Kg', '182,50 MT']),
        t(['A0004', 'BATATA', '9,96 Kg', '4 980,00 MT'], { alerta: true }),
        t(['Subtotal Ingredientes', '', '', '5 162,50 MT'], { subtotal: true }),
        { ...linha('VALOR TOTAL DO STOCK', '5 162,50 MT'), total: true, grande: true },
        { ...linha('Linhas a vermelho: abaixo do mínimo.'), soA4: true },
      ],
    }
    const a4 = htmlDocumento(stock, 210, null)
    expect(a4).toContain('<div class="titulo"><h1>EXISTÊNCIAS EM STOCK</h1><span>Em 18/11/2026</span></div>')
    expect(a4).toContain('<div class="cartao a"><div class="r">Abaixo do mínimo</div><div class="v">1</div></div>')
    expect(a4).toContain('<div class="faixa"><span>Ingredientes</span><span class="info">2 artigos · 5 162,50 MT</span></div>')
    expect(a4).toContain('<tr class="z a"><td class="t">A0004</td><td class="t">BATATA</td>')
    expect(a4).toContain('<tr class="s"><td class="t" colspan="2">Subtotal Ingredientes</td>')
    expect(a4).toContain('<div class="total"><span>VALOR TOTAL DO STOCK</span><span class="v">5 162,50 MT</span></div>')
    expect(a4).toContain('Linhas a vermelho')

    const talao = htmlDocumento(stock, 80, null)
    expect(talao).toContain('<div class="n c">EXISTÊNCIAS EM STOCK</div><div class="c">Em 18/11/2026</div>')
    expect(talao).toContain('<td class="desc">A0004 · BATATA (!)</td>')
    expect(talao).not.toContain('Linhas a vermelho')
    expect(talao).not.toContain('class="faixa"')
  })

  it('quebra de página: folha nova em A4, ignorada nos talões', () => {
    const folha: DocumentoImpressao = { titulo: 'Folha', linhas: [linha('BEBIDAS'), { ...linha('CARNES'), quebraPagina: true }] }
    expect(htmlDocumento(folha, 210, null)).toContain('<div class="quebra"></div><div class="">CARNES</div>')
    expect(htmlDocumento(folha, 80, null)).not.toContain('<div class="quebra">')
  })

  it('linhas só dos talões (provedor do sistema): saem em 80 e 58 mm, não em A4', () => {
    const conta: DocumentoImpressao = { titulo: 'Conta', linhas: [linha('TOTAL', '100,00 MT'), { ...linha('Provedor do sistema: LAM Company'), soTalao: true }] }
    expect(htmlDocumento(conta, 80, null)).toContain('Provedor do sistema: LAM Company')
    expect(htmlDocumento(conta, 58, null)).toContain('Provedor do sistema: LAM Company')
    expect(htmlDocumento(conta, 210, null)).not.toContain('LAM Company')
  })
})

describe('impressão no programa do posto (LAMGest, WebView2)', () => {
  const documento: DocumentoImpressao = { titulo: 'Recibo', linhas: [linha('TOTAL', '100,00 MT')] }
  type Mensagem = { tipo: string; id: string; html: string; largura: number }

  function programa(responder: (m: Mensagem) => { ok: boolean } | null) {
    const ouvintes = new Set<(e: MessageEvent) => void>()
    const enviadas: Mensagem[] = []
    const motor = {
      postMessage: (m: Mensagem) => {
        enviadas.push(m)
        const r = responder(m)
        if (r) queueMicrotask(() => ouvintes.forEach((o) => o(new MessageEvent('message', { data: { tipo: 'impresso', id: m.id, ...r } }))))
      },
      addEventListener: (_: string, o: (e: MessageEvent) => void) => ouvintes.add(o),
      removeEventListener: (_: string, o: (e: MessageEvent) => void) => ouvintes.delete(o),
    }
    Object.assign(window, { lamgestJanela: { versao: 1, impressaoDirecta: true }, chrome: { webview: motor } })
    return enviadas
  }

  afterEach(() => {
    delete (window as unknown as Record<string, unknown>).lamgestJanela
    delete (window as unknown as Record<string, unknown>).chrome
    document.body.innerHTML = ''
  })

  it('com impressão directa, o programa imprime o documento (sem a janela de impressão nem a moldura)', async () => {
    const enviadas = programa(() => ({ ok: true }))
    imprimirNoNavegador(documento, 80, { logotipo: null, logotipo80mm: true, copias: { recibo: 2 } } as unknown as ConfiguracoesPublicas, 'recibo')
    await new Promise((r) => setTimeout(r, 0))
    expect(enviadas).toHaveLength(1)
    expect(enviadas[0]).toMatchObject({ tipo: 'imprimir', largura: 80 })
    expect(enviadas[0].html).not.toContain('window.print()')
    expect(enviadas[0].html.match(/class="copia"/g)).toHaveLength(2)
    expect(document.querySelector('iframe')).toBeNull()
  })

  it('se o programa não conseguir imprimir, abre a janela de impressão (moldura)', async () => {
    programa(() => ({ ok: false }))
    imprimirNoNavegador(documento, 58, null, 'recibo')
    await new Promise((r) => setTimeout(r, 0))
    expect(document.querySelector('iframe')).not.toBeNull()
  })

  it('sem impressão directa, usa a moldura como no navegador', () => {
    const enviadas = programa(() => ({ ok: true }))
    Object.assign(window, { lamgestJanela: { versao: 1, impressaoDirecta: false } })
    imprimirNoNavegador(documento, 80, null, 'recibo')
    expect(enviadas).toHaveLength(0)
    expect(document.querySelector('iframe')).not.toBeNull()
  })
})
