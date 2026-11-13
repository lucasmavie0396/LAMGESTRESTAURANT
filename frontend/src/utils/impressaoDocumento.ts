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
  return [l.negrito ? 'n' : '', l.grande ? 'g' : '', l.alinhamento === 'Centro' ? 'c' : l.alinhamento === 'Direita' ? 'd' : ''].filter(Boolean).join(' ')
}

function linhaHtml(l: LinhaImpressao) {
  if (l.separador) return '<hr>'
  return l.direita === null
    ? `<div class="${classes(l)}">${esc(l.texto) || '&nbsp;'}</div>`
    : `<div class="l ${classes(l)}"><span>${esc(l.texto)}</span><span class="v">${esc(l.direita)}</span></div>`
}

/**
 * Tabela (linhas de tabela seguidas). A4 e talões com poucas colunas: uma linha por registo, valores à direita.
 * Talões com muitas colunas: a descrição numa linha e os valores em colunas por baixo (como no servidor).
 */
function tabelaHtml(linhas: LinhaImpressao[], largura: LarguraPapel) {
  const colunas = Math.max(...linhas.map((l) => l.celulas!.length))
  // Aproximação da regra do servidor (que mede o texto): nos talões, tabelas com mais de 3 colunas empilham.
  const empilhar = largura < 210 && colunas > 3
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

function corpoHtml(linhas: LinhaImpressao[], largura: LarguraPapel) {
  const partes: string[] = []
  for (let i = 0; i < linhas.length; i++) {
    const l = linhas[i]
    if (l.celulas && l.celulas.length > 2) {
      let fim = i
      while (fim + 1 < linhas.length && (linhas[fim + 1].celulas?.length ?? 0) > 2) fim++
      partes.push(tabelaHtml(linhas.slice(i, fim + 1), largura))
      i = fim
    } else if (l.celulas && l.celulas.length === 2) {
      partes.push(linhaHtml({ ...l, texto: l.celulas[0], direita: l.celulas[1] }).replace('class="l', `class="l${l.cabecalho ? ' sub' : ''}`))
    } else {
      partes.push(linhaHtml(l))
    }
  }
  return partes.join('')
}

/**
 * HTML do documento para a largura de papel indicada (exportado para testes). Mesma disposição do servidor:
 * o logótipo carregado sai em todos os documentos e papéis; em A4 fica à esquerda e a identificação do estabelecimento
 * à direita; nos talões ficam centrados.
 */
export function htmlDocumento(documento: DocumentoImpressao, largura: LarguraPapel, logotipo: string | null, copias = 1) {
  const a4 = largura >= 210
  const util = a4 ? '180mm' : largura === 58 ? '54mm' : '76mm'
  const fonte = a4 ? '10pt' : largura === 58 ? '7.5pt' : '9pt'
  const logo = logotipo ? `<img class="logo" src="${esc(logotipo)}" alt="">` : ''
  const cabecalho = (documento.cabecalho ?? []).map((l) => `<div class="${classes({ ...l, alinhamento: 'Esquerda' })}">${esc(l.texto)}</div>`).join('')
  const topo = logo || cabecalho ? `<header class="${a4 && logo ? 'lado' : ''}">${logo}<div class="emp">${cabecalho}</div></header>` : ''

  return `<!doctype html><html lang="pt"><head><meta charset="utf-8"><title>${esc(documento.titulo)}</title>
<style>
  @page { size: ${a4 ? 'A4' : `${largura}mm auto`}; margin: ${a4 ? '15mm' : '2mm'}; }
  body { font-family: Arial, Helvetica, sans-serif; font-size: ${fonte}; width: ${util}; margin: 0 auto; color: #000; }
  div { padding: 0.5px 0; line-height: 1.25; white-space: pre-wrap; } /* pre-wrap: mantém os recuos («   Ref.») */
  .n { font-weight: bold; } .g { font-size: 1.35em; font-weight: bold; }
  .c { text-align: center; } .d { text-align: right; }
  .l { display: flex; justify-content: space-between; gap: 8px; } .l .v { white-space: nowrap; text-align: right; }
  .l.sub { border-bottom: 1px solid #000; }
  hr { border: 0; border-top: 1px ${a4 ? 'solid #bbb' : 'dashed #000'}; margin: 3px 0; }
  header { text-align: center; margin-bottom: 4px; padding-bottom: 4px; border-bottom: 1px ${a4 ? 'solid #000' : 'dashed #000'}; }
  header .logo { display: block; margin: 0 auto 4px; max-width: 75%; max-height: ${largura <= 58 ? '18mm' : '23mm'}; }
  header.lado { display: flex; justify-content: space-between; align-items: flex-start; text-align: right; }
  header.lado .logo { margin: 0; max-width: 55mm; max-height: 28mm; }
  table { width: 100%; border-collapse: collapse; margin: 2px 0; }
  td { padding: ${a4 ? '1.2mm 1mm' : '0.3mm 0'}; vertical-align: top; }
  td.v { text-align: right; white-space: nowrap; padding-left: 2mm; }
  tr.th td { font-weight: bold; ${a4 ? 'background: #e8ecf1;' : 'border-bottom: 1px solid #000;'} }
  ${a4 ? 'tr:not(.th) td { border-bottom: 1px solid #ddd; } thead { display: table-header-group; }' : ''}
  table.emp tr.valores td { padding-bottom: 0.8mm; }
  table.emp tr.th.valores td { border-bottom: 1px solid #000; } table.emp tr.th:not(.valores) td { border-bottom: 0; }
  tr { break-inside: avoid; }
  .copia + .copia { break-before: page; }
</style></head><body>${Array.from({ length: Math.max(1, copias) }, () => `<section class="copia">${topo}${corpoHtml(documento.linhas, largura)}</section>`).join('')}
<script>window.onload = function () { window.focus(); window.print(); };</script>
</body></html>`
}

/**
 * Imprime o documento neste posto, com a largura de papel escolhida (talão 58/80 mm ou A4), numa moldura escondida:
 * não abre janelas (que o navegador bloqueia quando a impressão não vem de um clique, ex.: o recibo automático).
 * Nos postos instalados com «impressão directa» (Chrome com --kiosk-printing) sai sem janela na impressora predefinida.
 * O logótipo e o número de cópias seguem as Configurações → Documentos e POS.
 */
export function imprimirNoNavegador(documento: DocumentoImpressao, largura: LarguraPapel, publicas: ConfiguracoesPublicas | null | undefined, chaveCopias: string) {
  const logotipo = largura === 80 && publicas?.logotipo80mm === false ? null : publicas?.logotipo ?? null
  const moldura = document.createElement('iframe')
  moldura.setAttribute('aria-hidden', 'true')
  moldura.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden'
  document.body.appendChild(moldura)
  const doc = moldura.contentDocument!
  doc.open()
  doc.write(htmlDocumento(documento, largura, logotipo, publicas?.copias?.[chaveCopias] ?? 1)) // o documento imprime-se a si próprio quando acaba de carregar
  doc.close()
  moldura.contentWindow?.addEventListener('afterprint', () => setTimeout(() => moldura.remove(), 1000))
  setTimeout(() => moldura.remove(), 120_000) // garantia, se o navegador não avisar o fim da impressão
}
