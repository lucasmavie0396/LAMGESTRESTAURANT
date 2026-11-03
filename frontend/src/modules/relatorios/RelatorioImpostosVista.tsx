import { useState } from 'react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { RelatorioImpostos } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { Indicadores, Metodologia, Seccao } from './comum'
import { tabela } from './formato'

type Agrupar = 'auto' | 'dia' | 'mes'

/** IVA incluído por taxa e por dia/mês, com os descontos; as taxas são as gravadas em cada venda. */
export function RelatorioImpostosVista({ desde, ate }: { desde: string; ate: string }) {
  const [agrupar, setAgrupar] = useState<Agrupar>('auto')
  const rel = useDados(() => api.get<RelatorioImpostos>('/api/relatorios/impostos' + query({ desde, ate, agrupar: agrupar === 'auto' ? '' : agrupar })),
    [desde, ate, agrupar])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const sufixo = `${desde}_${ate}`

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Total com imposto', formatarMoeda(r.total), `${r.vendas} vendas`],
        ['Base sem imposto', formatarMoeda(r.baseSemImposto), r.taxasEntrega ? `+ taxas de entrega ${formatarMoeda(r.taxasEntrega)}` : 'Sem taxas de entrega'],
        ['Imposto incluído', formatarMoeda(r.impostos), 'IVA a declarar (estimativa)'],
        ['Descontos', formatarMoeda(r.descontos), `Subtotal antes de descontos: ${formatarMoeda(r.subtotal)}`],
      ]} />

      <Seccao titulo="Por taxa"
        exportar={() => tabela(`iva-por-taxa_${sufixo}`, ['Taxa', 'Base sem imposto (MT)', 'Imposto (MT)', 'Com imposto (MT)'],
          [...r.porTaxa.map((t) => [t.taxa, t.baseSemImposto, t.imposto, t.comImposto]),
            ['Taxas de entrega (sem imposto)', r.taxasEntrega, 0, r.taxasEntrega], ['TOTAL', r.baseSemImposto, r.impostos, r.total]])}>
        {r.porTaxa.length === 0 ? <Vazio texto="Sem vendas no período." /> : (
          <table className="w-full text-left text-sm">
            <thead className="text-slate-600">
              <tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Taxa</th>
                <th scope="col" className="py-2 text-right">Base sem imposto</th>
                <th scope="col" className="py-2 text-right">Imposto</th>
                <th scope="col" className="py-2 text-right">Com imposto</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {r.porTaxa.map((t) => (
                <tr key={t.percentagem}>
                  <td className="py-1.5 font-medium">{t.taxa}</td>
                  <td className="py-1.5 text-right">{formatarMoeda(t.baseSemImposto)}</td>
                  <td className="py-1.5 text-right">{formatarMoeda(t.imposto)}</td>
                  <td className="py-1.5 text-right">{formatarMoeda(t.comImposto)}</td>
                </tr>
              ))}
              {r.taxasEntrega > 0 && (
                <tr className="text-slate-600">
                  <td className="py-1.5">Taxas de entrega (sem imposto)</td>
                  <td className="py-1.5 text-right">{formatarMoeda(r.taxasEntrega)}</td>
                  <td className="py-1.5 text-right">—</td>
                  <td className="py-1.5 text-right">{formatarMoeda(r.taxasEntrega)}</td>
                </tr>
              )}
            </tbody>
            <tfoot className="border-t-2 border-slate-300 font-semibold tabular-nums">
              <tr>
                <td className="py-2">Total</td>
                <td className="py-2 text-right">{formatarMoeda(r.baseSemImposto)}</td>
                <td className="py-2 text-right">{formatarMoeda(r.impostos)}</td>
                <td className="py-2 text-right">{formatarMoeda(r.total)}</td>
              </tr>
            </tfoot>
          </table>
        )}
      </Seccao>

      <Seccao titulo={r.agrupamento === 'mes' ? 'Por mês' : 'Por dia'}
        accoes={
          <div className="flex rounded-lg ring-1 ring-slate-300 print:hidden" role="group" aria-label="Agrupar por">
            {([['auto', 'Automático'], ['dia', 'Dia'], ['mes', 'Mês']] as const).map(([v, t]) => (
              <button key={v} type="button" onClick={() => setAgrupar(v)} aria-pressed={agrupar === v}
                className={`min-h-9 px-3 text-xs font-medium first:rounded-l-lg last:rounded-r-lg ${agrupar === v ? 'bg-primaria-600 text-white' : 'bg-white'}`}>{t}</button>
            ))}
          </div>
        }
        exportar={() => tabela(`iva-por-${r.agrupamento}_${sufixo}`, [r.agrupamento === 'mes' ? 'Mês' : 'Dia', 'Vendas', 'Descontos (MT)', 'Base sem imposto (MT)', 'Imposto (MT)', 'Taxas de entrega (MT)', 'Total (MT)'],
          r.porPeriodo.map((p) => [p.periodo, p.vendas, p.descontos, p.baseSemImposto, p.imposto, p.taxasEntrega, p.total]))}>
        {r.porPeriodo.length === 0 ? <Vazio texto="Sem vendas no período." /> : (
          <div className="max-h-96 overflow-auto print:max-h-none">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">{r.agrupamento === 'mes' ? 'Mês' : 'Dia'}</th>
                  <th scope="col" className="py-2 text-right">Vendas</th>
                  <th scope="col" className="py-2 text-right">Descontos</th>
                  <th scope="col" className="py-2 text-right">Base</th>
                  <th scope="col" className="py-2 text-right">Imposto</th>
                  <th scope="col" className="py-2 text-right">Total</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.porPeriodo.map((p) => (
                  <tr key={p.periodo}>
                    <td className="py-1.5 font-medium">{p.periodo}</td>
                    <td className="py-1.5 text-right">{p.vendas}</td>
                    <td className="py-1.5 text-right text-slate-600">{formatarMoeda(p.descontos)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(p.baseSemImposto)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(p.imposto)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(p.total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>

      <Metodologia>
        Os preços incluem o imposto; o imposto de cada linha é total × taxa / (100 + taxa), com a taxa gravada na venda. O desconto do pedido reduz o imposto
        na mesma proporção. A taxa de entrega não leva imposto. Os arredondamentos por taxa são acertados para a soma bater com o imposto de cada pedido.
        Este relatório é interno e não substitui a declaração fiscal certificada.
      </Metodologia>
    </div>
  )
}
