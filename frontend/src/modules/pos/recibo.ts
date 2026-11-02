import type { Recibo } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'

const n2 = new Intl.NumberFormat('pt-MZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const n3 = new Intl.NumberFormat('pt-MZ', { maximumFractionDigits: 3 })

function esc(texto: string | null | undefined) {
  return (texto ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!)
}

/**
 * Imprime o recibo interno numa janela própria com largura de talão (80 mm).
 * Funciona com qualquer impressora instalada no Windows (incluindo térmicas com driver);
 * a impressão directa ESC/POS em rede fica documentada como evolução futura.
 */
export function imprimirRecibo(r: Recibo) {
  const linhas = r.linhas
    .map((l) => `<tr><td colspan="3">${esc(l.produto)}</td></tr>
      <tr><td>${n3.format(l.quantidade)} × ${n2.format(l.precoUnitario)}</td><td></td><td class="d">${n2.format(l.total)}</td></tr>`)
    .join('')
  const pagamentos = r.pagamentos
    .map((p) => `<tr><td>${esc(p.metodo)}${p.referencia ? ` (${esc(p.referencia)})` : ''}</td><td></td><td class="d">${n2.format(p.valorRecebido)}</td></tr>
      ${p.troco > 0 ? `<tr><td>Troco</td><td></td><td class="d">${n2.format(p.troco)}</td></tr>` : ''}`)
    .join('')

  const html = `<!doctype html><html lang="pt"><head><meta charset="utf-8"><title>Recibo ${esc(r.numero)}</title>
<style>
  @page { size: 80mm auto; margin: 4mm; }
  body { font-family: "Courier New", monospace; font-size: 12px; width: 72mm; margin: 0 auto; color: #000; }
  h1 { font-size: 15px; text-align: center; margin: 0 0 2px; }
  .c { text-align: center; } .d { text-align: right; white-space: nowrap; }
  table { width: 100%; border-collapse: collapse; } td { padding: 1px 0; vertical-align: top; }
  hr { border: 0; border-top: 1px dashed #000; margin: 6px 0; }
  .total td { font-size: 14px; font-weight: bold; }
</style></head><body>
  <h1>${esc(r.estabelecimento)}</h1>
  ${r.endereco ? `<div class="c">${esc(r.endereco)}</div>` : ''}
  ${r.telefone ? `<div class="c">Tel: ${esc(r.telefone)}</div>` : ''}
  ${r.nuit ? `<div class="c">NUIT: ${esc(r.nuit)}</div>` : ''}
  <hr>
  <div>Recibo: ${esc(r.numero)}</div>
  <div>Pedido: ${esc(r.pedidoNumero)}${r.mesa ? ` · Mesa ${esc(r.mesa)}` : ` · ${r.tipo === 'TakeAway' ? 'Take Away' : esc(r.tipo)}`}</div>
  <div>Data: ${esc(formatarDataHora(r.emitidoEmUtc))}</div>
  ${r.operador ? `<div>Operador: ${esc(r.operador)}</div>` : ''}
  <hr><table>${linhas}</table><hr>
  <table>
    <tr><td>Subtotal</td><td></td><td class="d">${n2.format(r.subtotal)}</td></tr>
    ${r.desconto > 0 ? `<tr><td>Desconto</td><td></td><td class="d">-${n2.format(r.desconto)}</td></tr>` : ''}
    ${r.taxaEntrega > 0 ? `<tr><td>Taxa de entrega</td><td></td><td class="d">${n2.format(r.taxaEntrega)}</td></tr>` : ''}
    <tr class="total"><td>TOTAL ${esc(r.moeda)}</td><td></td><td class="d">${n2.format(r.total)}</td></tr>
    <tr><td>Impostos incluídos</td><td></td><td class="d">${n2.format(r.impostos)}</td></tr>
  </table><hr>
  <table>${pagamentos}</table><hr>
  <div class="c">${esc(r.rodape ?? 'Documento interno - não serve de factura.')}</div>
  <div class="c">Obrigado pela preferência!</div>
  <script>window.onload = function () { window.print(); setTimeout(function () { window.close(); }, 500); };</script>
</body></html>`

  const janela = window.open('', '_blank', 'width=400,height=600')
  if (!janela) throw new Error('O navegador bloqueou a janela de impressão. Permita janelas pop-up para este endereço.')
  janela.document.open()
  janela.document.write(html)
  janela.document.close()
}
