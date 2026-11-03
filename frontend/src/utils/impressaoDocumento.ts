import type { DocumentoImpressao, TipoDocumentoRelatorio } from '../types/api'

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
}

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

/** Mesma regra do servidor: recibos com logótipo em 80 mm e A4; relatórios só em A4; nunca em 58 mm. */
export function levaLogotipo(documento: DocumentoImpressao, largura: LarguraPapel) {
  return documento.logotipo === 'A4E80mm' ? largura >= 80 : documento.logotipo === 'SoA4' ? largura >= 210 : false
}

/** HTML do documento para a largura de papel indicada (exportado para testes). */
export function htmlDocumento(documento: DocumentoImpressao, largura: LarguraPapel, logotipo: string | null) {
  const a4 = largura >= 210
  const util = a4 ? '180mm' : largura === 58 ? '50mm' : '72mm'
  const fonte = a4 ? '11pt' : largura === 58 ? '9px' : '11px'
  const logo = logotipo && levaLogotipo(documento, largura)
    ? `<div class="logo"><img src="${esc(logotipo)}" alt=""></div>`
    : ''
  const linhas = documento.linhas.map((l) => {
    if (l.separador) return '<hr>'
    const classes = [l.negrito ? 'n' : '', l.grande ? 'g' : '', l.alinhamento === 'Centro' ? 'c' : l.alinhamento === 'Direita' ? 'd' : ''].filter(Boolean).join(' ')
    return l.direita === null
      ? `<div class="${classes}">${esc(l.texto) || '&nbsp;'}</div>`
      : `<div class="l ${classes}"><span>${esc(l.texto)}</span><span class="v">${esc(l.direita)}</span></div>`
  }).join('')

  return `<!doctype html><html lang="pt"><head><meta charset="utf-8"><title>${esc(documento.titulo)}</title>
<style>
  @page { size: ${a4 ? 'A4' : `${largura}mm auto`}; margin: ${a4 ? '15mm' : '2mm'}; }
  body { font-family: Arial, Helvetica, sans-serif; font-size: ${fonte}; width: ${util}; margin: 0 auto; color: #000; }
  div { padding: 1px 0; line-height: 1.25; white-space: pre-wrap; } /* pre-wrap: mantém os recuos («   Ref.») */
  .n { font-weight: bold; } .g { font-size: 1.35em; font-weight: bold; }
  .c { text-align: center; } .d { text-align: right; }
  .l { display: flex; justify-content: space-between; gap: 8px; } .l .v { white-space: nowrap; text-align: right; }
  hr { border: 0; border-top: 1px dashed #000; margin: 4px 0; }
  .logo { text-align: center; margin-bottom: 6px; } .logo img { max-width: ${a4 ? '60mm' : '75%'}; max-height: ${a4 ? '28mm' : '23mm'}; }
</style></head><body>${logo}${linhas}
<script>window.onload = function () { window.print(); setTimeout(function () { window.close(); }, 500); };</script>
</body></html>`
}

/**
 * Imprime o documento numa janela própria, com a largura de papel escolhida (talão 58/80 mm ou A4),
 * pela janela de impressão do Windows deste posto.
 */
export function imprimirNoNavegador(documento: DocumentoImpressao, largura: LarguraPapel, logotipo: string | null) {
  const janela = window.open('', '_blank', largura >= 210 ? 'width=900,height=900' : 'width=420,height=700')
  if (!janela) throw new Error('O navegador bloqueou a janela de impressão. Permita janelas pop-up para este endereço.')
  janela.document.open()
  janela.document.write(htmlDocumento(documento, largura, logotipo))
  janela.document.close()
}
