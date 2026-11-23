import type { ConfiguracoesPublicas, DocumentoImpressao, LinhaImpressao, TipoDocumento, TipoDocumentoRelatorio } from '../types/api'

/** Chave das cópias configuradas para cada documento comercial. */
export const COPIAS_DOCUMENTO: Record<TipoDocumento, string> = {
  VendaDinheiro: 'venda-dinheiro',
  Factura: 'factura',
  Recibo: 'recibo',
  Cotacao: 'cotacao',
  NotaCredito: 'nota-credito',
}

export const NOMES_DOCUMENTO: Record<TipoDocumentoRelatorio, string> = {
  Pagamentos: 'Relatório de pagamentos',
  Vendas: 'Resumo de vendas',
  Produtos: 'Produtos vendidos',
  Despesas: 'Relatório de despesas',
  Financeiro: 'Relatório financeiro',
  Stock: 'Existências em stock',
  FechoCaixa: 'Fecho de caixa',
  ListaVendas: 'Lista de vendas',
  Clientes: 'Vendas por cliente',
  Impostos: 'IVA e descontos',
  Anulacoes: 'Anulações e devoluções',
  CartoesEvento: 'Cartões Evento',
  FacturasCredito: 'Facturas a crédito e dívidas',
  HistoricoCliente: 'Histórico de facturas do cliente',
  ExtratoCliente: 'Extrato de conta corrente',
  DescargasCartoes: 'Descargas de cartões',
  FechoDia: 'Fecho do dia',
  FolhaContagem: 'Folha de contagem (inventário)',
  Acompanhamentos: 'Acompanhamentos escolhidos',
  Existencias: 'Existências por armazém',
  Transferencias: 'Transferências entre armazéns',
}

/** Relatórios que pedem um cliente (histórico e extrato da conta corrente). */
export const DOCUMENTOS_DE_CLIENTE: TipoDocumentoRelatorio[] = ['HistoricoCliente', 'ExtratoCliente']

/** Papéis suportados: talão de 58 ou 80 mm, ou folha A4 (210 mm). */
export type LarguraPapel = 58 | 80 | 210

export const PAPEIS: { valor: LarguraPapel; rotulo: string }[] = [
  { valor: 80, rotulo: 'Talão 80 mm' },
  { valor: 58, rotulo: 'Talão 58 mm' },
  { valor: 210, rotulo: 'Folha A4' },
]

function esc(texto: string | null | undefined) {
  return (texto ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!)
}

function classes(l: LinhaImpressao) {
  return [l.negrito ? 'n' : '', l.grande ? 'g' : '', l.alinhamento === 'Centro' ? 'c' : l.alinhamento === 'Direita' ? 'd' : '', l.alerta ? 'alerta' : '']
    .filter(Boolean).join(' ')
}

function linhaHtml(l: LinhaImpressao, extra = '') {
  if (l.separador) return '<hr>'
  const c = [classes(l), extra].filter(Boolean).join(' ')
  return l.direita === null || l.direita === undefined
    ? `<div class="${c}">${esc(l.texto) || '&nbsp;'}</div>`
    : `<div class="${['l', c].filter(Boolean).join(' ')}"><span>${esc(l.texto)}</span><span class="v">${esc(l.direita)}</span></div>`
}

const textoDe = (l: LinhaImpressao) => Math.min(Math.max(l.colunasTexto ?? 1, 1), l.celulas?.length ?? 1)

/**
 * Linhas como saem num talão (58/80 mm ou PDA), igual ao servidor: sem as linhas só de A4, título e período centrados
 * em duas linhas, alertas a negrito com «(!)» e as colunas de texto de cada tabela juntas na descrição.
 */
export function linhasTalao(linhas: LinhaImpressao[]): LinhaImpressao[] {
  const saida: LinhaImpressao[] = []
  for (const l of linhas) {
    if (l.soA4) continue
    if (l.titulo) {
      saida.push({ ...l, titulo: false, direita: null, alinhamento: 'Centro', negrito: true })
      if (l.direita) saida.push({ ...l, titulo: false, texto: l.direita, direita: null, alinhamento: 'Centro', negrito: false })
    } else if (l.celulas) {
      const k = textoDe(l)
      const descricao = l.celulas.slice(0, k).filter(Boolean).join(' · ') + (l.alerta ? ' (!)' : '')
      saida.push({ ...l, texto: descricao, celulas: [descricao, ...l.celulas.slice(k)], colunasTexto: 1, negrito: l.negrito || !!l.alerta })
    } else if (l.alerta) saida.push({ ...l, texto: `${l.texto} (!)`, negrito: true })
    else saida.push(l)
  }
  return saida
}

/**
 * Tabela nos talões (linhas de tabela seguidas): com poucas colunas, uma linha por registo e valores à direita;
 * com muitas, a descrição numa linha e os valores em colunas por baixo (como no servidor).
 */
function tabelaTalao(linhas: LinhaImpressao[]) {
  const colunas = Math.max(...linhas.map((l) => l.celulas!.length))
  // Aproximação da regra do servidor (que mede o texto): nos talões, tabelas com mais de 3 colunas empilham.
  const empilhar = colunas > 3
  const linhasHtml = linhas.map((l) => {
    const c = l.celulas!
    const valores = Array.from({ length: colunas - 1 }, (_, i) => `<td class="v">${esc(c[i + 1] ?? '')}</td>`).join('')
    const tipo = l.cabecalho ? 'th' : ''
    return empilhar
      ? `<tr class="${tipo}"><td class="desc" colspan="${colunas - 1}">${esc(c[0])}</td></tr><tr class="${tipo} valores">${valores}</tr>`
      : `<tr class="${tipo}"><td class="desc">${esc(c[0])}</td>${valores}</tr>`
  }).join('')
  return `<table class="${empilhar ? 'emp' : ''}">${linhasHtml}</table>`
}

function corpoTalao(todas: LinhaImpressao[]) {
  const linhas = linhasTalao(todas)
  const partes: string[] = []
  for (let i = 0; i < linhas.length; i++) {
    const l = linhas[i]
    if (l.celulas && l.celulas.length > 2) {
      let fim = i
      while (fim + 1 < linhas.length && (linhas[fim + 1].celulas?.length ?? 0) > 2) fim++
      partes.push(tabelaTalao(linhas.slice(i, fim + 1)))
      i = fim
    } else if (l.celulas && l.celulas.length === 2) {
      partes.push(linhaHtml({ ...l, texto: l.celulas[0], direita: l.celulas[1] }, l.cabecalho ? 'sub' : ''))
    } else {
      partes.push(linhaHtml(l, l.faixa ? 'sub faixa-t' : ''))
    }
  }
  return partes.join('')
}

// ---------------- A4 ----------------

/** Largura útil da folha A4 (mm) e estimativa da largura de um texto a 10 pt (Arial ≈ 2 mm por carácter, por excesso), com a folga da célula. */
const UTIL_A4 = 180
const mmTexto = (t: string) => t.length * 2.05 + 4

/** Mesma regra do servidor: valores com a largura do maior conteúdo (mín. 16 mm); textos até 28%, o mais comprido fica com o resto. */
function largurasA4(linhas: LinhaImpressao[], colunas: number, texto: number): number[] {
  const maximo = Array.from({ length: colunas }, (_, c) =>
    Math.max(0, ...linhas.filter((l) => c >= texto || !l.subtotal).map((l) => mmTexto(l.celulas![c] ?? ''))))
  let flexivel = 0
  for (let c = 1; c < texto; c++) if (maximo[c] > maximo[flexivel]) flexivel = c
  const larguras = maximo.map((m, c) => (c === flexivel ? 0 : c < texto ? Math.min(m, UTIL_A4 * 0.28) : Math.max(16, m)))
  let fixas = larguras.reduce((s, x) => s + x, 0)
  if (fixas > UTIL_A4 * 0.75) {
    for (let c = 0; c < larguras.length; c++) larguras[c] *= (UTIL_A4 * 0.75) / fixas
    fixas = UTIL_A4 * 0.75
  }
  larguras[flexivel] = UTIL_A4 - fixas
  return larguras
}

const formaDe = (grelha: LinhaImpressao[]) => {
  const colunas = Math.max(...grelha.map((l) => l.celulas!.length))
  return { colunas, texto: Math.min(Math.max(1, ...grelha.map((l) => l.colunasTexto ?? 1)), colunas) }
}

/** Grelha A4: títulos das colunas (repetidos em cada página), linhas alternadas, alertas a vermelho e subtotal. */
function tabelaA4(linhas: LinhaImpressao[], larguras: number[], texto: number) {
  const colunas = larguras.length
  const cols = larguras.map((w) => `<col style="width:${((w / UTIL_A4) * 100).toFixed(2)}%">`).join('')
  let dados = 0
  const linha = (l: LinhaImpressao) => {
    const c = l.celulas!
    const zebra = !l.cabecalho && !l.subtotal && dados++ % 2 === 1
    const classe = [l.cabecalho ? 'th' : '', l.subtotal ? 's' : '', zebra ? 'z' : '', l.alerta ? 'a' : '', l.negrito ? 'n' : ''].filter(Boolean).join(' ')
    const celula = (i: number) => `<td class="${i < texto ? 't' : 'v'}">${esc(c[i] ?? '')}</td>`
    const celulas = l.subtotal && texto > 1
      ? `<td class="t" colspan="${texto}">${esc(c.slice(0, texto).filter(Boolean).join(' '))}</td>${Array.from({ length: colunas - texto }, (_, i) => celula(texto + i)).join('')}`
      : Array.from({ length: colunas }, (_, i) => celula(i)).join('')
    return `<tr class="${classe}">${celulas}</tr>`
  }
  const cabecalho = linhas.filter((l) => l.cabecalho).map(linha).join('')
  const corpo = linhas.filter((l) => !l.cabecalho).map(linha).join('')
  return `<table class="grelha"><colgroup>${cols}</colgroup>${cabecalho ? `<thead>${cabecalho}</thead>` : ''}<tbody>${corpo}</tbody></table>`
}

function corpoA4(todas: LinhaImpressao[]) {
  const linhas = todas.filter((l) => !l.soTalao)
  const ehTabela = (l: LinhaImpressao) => (l.celulas?.length ?? 0) >= 2
  // Grelhas seguidas; as de mesma forma têm as mesmas larguras em todo o documento.
  const grelhas: [number, number][] = []
  for (let i = 0; i < linhas.length; i++) {
    if (!ehTabela(linhas[i])) continue
    let fim = i
    while (fim + 1 < linhas.length && ehTabela(linhas[fim + 1])) fim++
    grelhas.push([i, fim])
    i = fim
  }
  const porForma = new Map<string, LinhaImpressao[]>()
  for (const [i, fim] of grelhas) {
    const g = linhas.slice(i, fim + 1)
    const f = formaDe(g)
    const chave = `${f.colunas}/${f.texto}`
    porForma.set(chave, [...(porForma.get(chave) ?? []), ...g])
  }
  const larguras = new Map([...porForma].map(([chave, g]) => {
    const [colunas, texto] = chave.split('/').map(Number)
    return [chave, largurasA4(g, colunas, texto)] as const
  }))

  const partes: string[] = []
  for (let i = 0; i < linhas.length; i++) {
    const l = linhas[i]
    // Quebra de página (A4): a linha começa uma folha nova.
    if (l.quebraPagina) partes.push('<div class="quebra"></div>')
    if (ehTabela(l)) {
      const fim = grelhas.find(([inicio]) => inicio === i)![1]
      const g = linhas.slice(i, fim + 1)
      const f = formaDe(g)
      partes.push(tabelaA4(g, larguras.get(`${f.colunas}/${f.texto}`)!, f.texto))
      i = fim
    } else if (l.cartao) {
      let fim = i
      while (fim + 1 < linhas.length && linhas[fim + 1].cartao) fim++
      const cartoes = linhas.slice(i, fim + 1)
      partes.push(`<div class="cartoes" style="grid-template-columns:repeat(${Math.min(4, cartoes.length)},1fr)">${cartoes
        .map((c) => `<div class="cartao${c.alerta ? ' a' : ''}"><div class="r">${esc(c.texto)}</div><div class="v">${esc(c.direita)}</div></div>`).join('')}</div>`)
      i = fim
    } else if (l.faixa) {
      partes.push(`<div class="faixa"><span>${esc(l.texto)}</span>${l.direita ? `<span class="info">${esc(l.direita)}</span>` : ''}</div>`)
    } else if (l.titulo) {
      partes.push(`<div class="titulo"><h1>${esc(l.texto)}</h1>${l.direita ? `<span>${esc(l.direita)}</span>` : ''}</div>`)
    } else if (l.total) {
      partes.push(`<div class="total"><span>${esc(l.texto)}</span><span class="v">${esc(l.direita)}</span></div>`)
    } else {
      partes.push(linhaHtml(l))
    }
  }
  return partes.join('')
}

/**
 * HTML do documento para a largura de papel indicada (exportado para testes). Mesma disposição do servidor:
 * o logótipo carregado sai em todos os documentos e papéis; em A4 fica à esquerda e a identificação do estabelecimento
 * à direita, e os relatórios saem em grelhas (faixa com o título, colunas, subtotal, quadros de resumo e total); nos talões
 * fica tudo centrado e compacto.
 */
export function htmlDocumento(documento: DocumentoImpressao, largura: LarguraPapel, logotipo: string | null, copias = 1, imprimirAoCarregar = true) {
  const a4 = largura >= 210
  const util = a4 ? `${UTIL_A4}mm` : largura === 58 ? '54mm' : '76mm'
  const fonte = a4 ? '10pt' : largura === 58 ? '7.5pt' : '9pt'
  const logo = logotipo ? `<img class="logo" src="${esc(logotipo)}" alt="">` : ''
  const cabecalho = (documento.cabecalho ?? []).map((l) => `<div class="${classes({ ...l, alinhamento: 'Esquerda' })}">${esc(l.texto)}</div>`).join('')
  const topo = logo || cabecalho ? `<header class="${a4 && logo ? 'lado' : ''}">${logo}<div class="emp">${cabecalho}</div></header>` : ''
  const corpo = a4 ? corpoA4(documento.linhas) : corpoTalao(documento.linhas)

  return `<!doctype html><html lang="pt"><head><meta charset="utf-8"><title>${esc(documento.titulo)}</title>
<style>
  @page { size: ${a4 ? 'A4' : `${largura}mm auto`}; margin: ${a4 ? '15mm' : '2mm'}; }
  * { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  body { font-family: Arial, Helvetica, sans-serif; font-size: ${fonte}; width: ${util}; margin: 0 auto; color: #000; background: #fff; }
  div { padding: 0.5px 0; line-height: 1.25; white-space: pre-wrap; } /* pre-wrap: mantém os recuos («   Ref.») */
  .n { font-weight: bold; } .g { font-size: 1.35em; font-weight: bold; }
  .c { text-align: center; } .d { text-align: right; }
  .l { display: flex; justify-content: space-between; gap: 8px; } .l .v { white-space: nowrap; text-align: right; }
  .l.sub, div.sub { border-bottom: 1px solid #000; } .faixa-t { margin-top: 1mm; font-weight: bold; }
  .alerta { font-weight: bold; ${a4 ? 'color: #a61b1b;' : ''} }
  hr { border: 0; border-top: 1px ${a4 ? 'solid #cdd4e0' : 'dashed #000'}; margin: 3px 0; }
  header { text-align: center; margin-bottom: 4px; padding-bottom: 4px; border-bottom: ${a4 ? '2px solid #1f3a68' : '1px dashed #000'}; }
  header .logo { display: block; margin: 0 auto 4px; max-width: 75%; max-height: ${largura <= 58 ? '18mm' : '23mm'}; }
  header.lado { display: flex; justify-content: space-between; align-items: flex-start; text-align: right; }
  header.lado .logo { margin: 0; max-width: 55mm; max-height: 28mm; }
  table { width: 100%; border-collapse: collapse; margin: 2px 0; }
  td { padding: ${a4 ? '1.2mm 1.5mm' : '0.3mm 0'}; vertical-align: top; }
  td.v { text-align: right; white-space: nowrap; padding-left: 2mm; }
  tr.th td { font-weight: bold; ${a4 ? '' : 'border-bottom: 1px solid #000;'} }
  table.emp tr.valores td { padding-bottom: 0.8mm; }
  table.emp tr.th.valores td { border-bottom: 1px solid #000; } table.emp tr.th:not(.valores) td { border-bottom: 0; }
  tr { break-inside: avoid; }
  .copia + .copia { break-before: page; }
  .quebra { break-before: page; padding: 0; }
  /* A4: título, quadros de resumo, grelhas com faixa e total */
  .titulo { display: flex; justify-content: space-between; align-items: baseline; gap: 6mm; margin: 2mm 0 3mm; }
  .titulo h1 { margin: 0; font-size: 1.5em; color: #1f3a68; } .titulo span { font-weight: bold; white-space: nowrap; }
  .cartoes { display: grid; gap: 3mm; margin-bottom: 4mm; }
  .cartao { border: 1px solid #cdd4e0; padding: 2.5mm 2.5mm; break-inside: avoid; }
  .cartao .r { font-size: 0.8em; color: #5a5a5a; text-transform: uppercase; letter-spacing: .3px; }
  .cartao .v { font-size: 1.35em; font-weight: bold; margin-top: 0.8mm; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .cartao.a { border-color: #e0a3a3; background: #fdf3f3; } .cartao.a .v { color: #a61b1b; }
  .faixa { display: flex; justify-content: space-between; align-items: baseline; gap: 4mm; background: #1f3a68; color: #fff; padding: 1.4mm 2mm;
    margin-top: 3mm; font-weight: bold; text-transform: uppercase; break-after: avoid; }
  .faixa .info { font-weight: normal; text-transform: none; white-space: nowrap; }
  table.grelha { table-layout: fixed; margin: 0 0 1mm; }
  table.grelha thead { display: table-header-group; }
  table.grelha td { border-bottom: 1px solid #cdd4e0; overflow-wrap: anywhere; white-space: normal; }
  table.grelha td.v { white-space: nowrap; overflow-wrap: normal; }
  table.grelha tr.th td { background: #e8edf5; color: #1f3a68; }
  table.grelha tr.z td { background: #f6f8fb; }
  table.grelha tr.n td { font-weight: bold; }
  table.grelha tr.a td { color: #a61b1b; font-weight: bold; } table.grelha tr.a td:first-child { box-shadow: inset 0.8mm 0 0 #c62828; }
  table.grelha tr.s td { background: #ecf1f8; font-weight: bold; border-top: 1.5px solid #1f3a68; }
  .total { display: flex; justify-content: space-between; align-items: center; gap: 4mm; border: 2px solid #1f3a68; padding: 2.2mm 4mm;
    margin: 2mm 0 1.5mm; font-size: 1.35em; font-weight: bold; break-inside: avoid; }
  .total .v { color: #1f3a68; white-space: nowrap; }
</style></head><body>${Array.from({ length: Math.max(1, copias) }, () => `<section class="copia">${topo}${corpo}</section>`).join('')}
${imprimirAoCarregar ? '<script>window.onload = function () { window.focus(); window.print(); };</script>\n' : ''}</body></html>`
}

/** Programa do posto (LAMGest, WebView2): anuncia-se em cada página e, com impressão directa, imprime sem janela. */
interface JanelaPosto { versao: number; impressaoDirecta: boolean }
interface MotorJanela {
  postMessage(mensagem: unknown): void
  addEventListener(tipo: 'message', ouvinte: (e: MessageEvent) => void): void
  removeEventListener(tipo: 'message', ouvinte: (e: MessageEvent) => void): void
}
function janelaPosto(): { janela: JanelaPosto; motor: MotorJanela } | null {
  const w = window as unknown as { lamgestJanela?: JanelaPosto; chrome?: { webview?: MotorJanela } }
  return w.lamgestJanela?.impressaoDirecta && w.chrome?.webview ? { janela: w.lamgestJanela, motor: w.chrome.webview } : null
}

/**
 * No programa do posto com impressão directa: o programa imprime o documento sem janela na impressora predefinida.
 * Resolve com true se imprimiu; false se falhou (a impressora não está disponível…), para se usar a janela de impressão.
 */
function imprimirNoPrograma(motor: MotorJanela, html: string, largura: LarguraPapel): Promise<boolean> {
  const id = typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : String(Date.now() + Math.random())
  return new Promise((resolver) => {
    const fim = setTimeout(() => { motor.removeEventListener('message', ouvir); resolver(false) }, 90_000)
    function ouvir(e: MessageEvent) {
      const d = e.data as { tipo?: string; id?: string; ok?: boolean } | null
      if (d?.tipo !== 'impresso' || d.id !== id) return
      clearTimeout(fim)
      motor.removeEventListener('message', ouvir)
      resolver(d.ok === true)
    }
    motor.addEventListener('message', ouvir)
    motor.postMessage({ tipo: 'imprimir', id, html, largura })
  })
}


/**
 * Imprime o documento neste posto, com a largura de papel escolhida (talão 58/80 mm ou A4), numa moldura escondida:
 * não abre janelas (que o navegador bloqueia quando a impressão não vem de um clique, ex.: o recibo automático).
 * Nos postos instalados com «impressão directa» sai sem janela na impressora predefinida: no programa do posto, pelo próprio
 * programa; no Chrome com --kiosk-printing, pela moldura. Se o programa não conseguir imprimir, abre a janela de impressão.
 * O logótipo e o número de cópias seguem as Configurações → Documentos e POS.
 */
export function imprimirNoNavegador(documento: DocumentoImpressao, largura: LarguraPapel, publicas: ConfiguracoesPublicas | null | undefined, chaveCopias: string) {
  const logotipo = largura === 80 && publicas?.logotipo80mm === false ? null : publicas?.logotipo ?? null
  const copias = publicas?.copias?.[chaveCopias] ?? 1
  const programa = janelaPosto()
  if (programa) {
    void imprimirNoPrograma(programa.motor, htmlDocumento(documento, largura, logotipo, copias, false), largura)
      .then((ok) => { if (!ok) imprimirNaMoldura(htmlDocumento(documento, largura, logotipo, copias)) })
    return
  }
  imprimirNaMoldura(htmlDocumento(documento, largura, logotipo, copias))
}

function imprimirNaMoldura(html: string) {
  const moldura = document.createElement('iframe')
  moldura.setAttribute('aria-hidden', 'true')
  moldura.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden'
  document.body.appendChild(moldura)
  const doc = moldura.contentDocument!
  doc.open()
  doc.write(html) // o documento imprime-se a si próprio quando acaba de carregar
  doc.close()
  moldura.contentWindow?.addEventListener('afterprint', () => setTimeout(() => moldura.remove(), 1000))
  setTimeout(() => moldura.remove(), 120_000) // garantia, se o navegador não avisar o fim da impressão
}
