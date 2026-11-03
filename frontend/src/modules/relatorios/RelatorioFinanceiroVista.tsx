import { api } from '../../api/cliente'
import { BarrasHorizontais } from '../../components/Graficos'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { RelatorioFinanceiro } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { Indicadores, Metodologia, Seccao } from './comum'
import { corSinal, tabela } from './formato'

export function RelatorioFinanceiroVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useDados(() => api.get<RelatorioFinanceiro>(`/api/relatorios/financeiro?desde=${desde}&ate=${ate}`), [desde, ate])
  const sufixo = `${desde}_${ate}`

  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const diferencaTotal = r.sessoesFechadas.reduce((s, f) => s + f.diferenca, 0)
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Vendas', formatarMoeda(r.totalVendas), `${r.numeroPedidos} pedidos · sem impostos ${formatarMoeda(r.vendasSemImpostos)}`],
        ['Custo estimado das vendas', formatarMoeda(r.custoEstimadoVendas), 'Ingredientes consumidos / custo de referência'],
        ['Despesas', formatarMoeda(r.despesas), `${r.despesasPorCategoria.reduce((s, l) => s + l.quantidade, 0)} despesas confirmadas`],
        ['Resultado estimado', formatarMoeda(r.resultadoEstimado), 'Vendas sem impostos − custo − despesas'],
      ]} />

      <Seccao titulo="Demonstração resumida"
        exportar={() => tabela(`resultado_${sufixo}`, ['Rubrica', 'Valor (MT)'], [
          ['Vendas (com impostos)', r.totalVendas], ['Impostos incluídos', -r.impostos], ['Vendas sem impostos', r.vendasSemImpostos],
          ['Custo estimado das vendas', -r.custoEstimadoVendas], ['Despesas', -r.despesas], ['Resultado estimado', r.resultadoEstimado],
          ['Estornos (não contam como venda)', r.estornos], ['Compras confirmadas (informativo)', r.comprasConfirmadas],
        ])}>
        <dl className="max-w-xl space-y-1.5 text-sm tabular-nums">
          {([
            ['Vendas (com impostos)', r.totalVendas, false],
            ['Impostos incluídos', -r.impostos, false],
            ['Vendas sem impostos', r.vendasSemImpostos, true],
            ['Custo estimado das vendas', -r.custoEstimadoVendas, false],
            ['Despesas', -r.despesas, false],
            ['Resultado estimado', r.resultadoEstimado, true],
          ] as const).map(([rotulo, valor, destaque]) => (
            <div key={rotulo} className={`flex justify-between ${destaque ? 'border-t border-slate-200 pt-1.5 font-semibold' : ''}`}>
              <dt>{rotulo}</dt><dd className={corSinal(valor)}>{formatarMoeda(valor)}</dd>
            </div>
          ))}
        </dl>
        <p className="mt-3 text-xs text-slate-500">
          Fora do resultado: {r.pedidosEstornados} pedidos estornados ({formatarMoeda(r.estornos)}) e {r.numeroCompras} compras confirmadas ({formatarMoeda(r.comprasConfirmadas)}, com IVA).
        </p>
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-3">
        <Seccao titulo="Recebimentos por método"
          exportar={() => tabela(`recebimentos_${sufixo}`, ['Método', 'Pagamentos', 'Total (MT)'], r.recebimentosPorMetodo.map((m) => [m.rotulo, m.quantidade, m.valor]))}>
          <BarrasHorizontais linhas={r.recebimentosPorMetodo.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} vazio="Sem vendas no período." />
        </Seccao>
        <Seccao titulo="Despesas por categoria"
          exportar={() => tabela(`despesas-categoria_${sufixo}`, ['Categoria', 'Despesas', 'Total (MT)'], r.despesasPorCategoria.map((m) => [m.rotulo, m.quantidade, m.valor]))}>
          <BarrasHorizontais linhas={r.despesasPorCategoria.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} vazio="Sem despesas no período." />
        </Seccao>
        <Seccao titulo="Despesas por forma de pagamento">
          <BarrasHorizontais linhas={r.despesasPorMetodo.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} vazio="Sem despesas no período." />
        </Seccao>
      </div>

      <Seccao titulo={`Fechos de caixa (${r.sessoesFechadas.length})`}
        exportar={() => tabela(`fechos-caixa_${sufixo}`, ['Sessão', 'Terminal', 'Aberta por', 'Fechada por', 'Abertura', 'Fecho', 'Esperado (MT)', 'Contado (MT)', 'Diferença (MT)', 'Observações'],
          r.sessoesFechadas.map((s) => [s.sessaoId, s.terminal, s.abertaPor, s.fechadaPor ?? '', formatarDataHora(s.abertaEmUtc), formatarDataHora(s.fechadaEmUtc), s.esperado, s.contado, s.diferenca, s.observacoes ?? '']))}>
        {r.sessoesAbertas > 0 && <p className="mb-2 text-sm text-amber-700">{r.sessoesAbertas} sessão(ões) de caixa ainda aberta(s): só entram aqui depois de fechadas.</p>}
        {r.sessoesFechadas.length === 0 ? <Vazio texto="Nenhum caixa fechado no período." /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Sessão</th>
                  <th scope="col" className="py-2">Operador</th>
                  <th scope="col" className="py-2">Período</th>
                  <th scope="col" className="py-2 text-right">Esperado</th>
                  <th scope="col" className="py-2 text-right">Contado</th>
                  <th scope="col" className="py-2 text-right">Diferença</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.sessoesFechadas.map((s) => (
                  <tr key={s.sessaoId}>
                    <td className="py-1.5"><span className="font-medium">n.º {s.sessaoId}</span> <span className="text-slate-600">· {s.terminal}</span></td>
                    <td className="py-1.5">{s.abertaPor}{s.fechadaPor && s.fechadaPor !== s.abertaPor && <span className="text-slate-600"> → {s.fechadaPor}</span>}</td>
                    <td className="py-1.5 text-slate-600">{formatarDataHora(s.abertaEmUtc)} – {formatarDataHora(s.fechadaEmUtc)}
                      {s.observacoes && <span className="block text-xs italic">{s.observacoes}</span>}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(s.esperado)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(s.contado)}</td>
                    <td className={`py-1.5 text-right font-semibold ${s.diferenca < 0 ? 'text-red-700' : s.diferenca > 0 ? 'text-amber-700' : 'text-green-700'}`}>{formatarMoeda(s.diferenca)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-slate-200 font-semibold tabular-nums">
                  <td className="py-2" colSpan={5}>Diferença total</td>
                  <td className={`py-2 text-right ${corSinal(diferencaTotal)}`}>{formatarMoeda(diferencaTotal)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
        {r.diferencasPorMetodo.some((m) => m.diferenca !== 0) && (
          <div className="mt-3 flex flex-wrap gap-x-6 gap-y-1 text-sm">
            {r.diferencasPorMetodo.filter((m) => m.diferenca !== 0).map((m) => (
              <span key={m.metodo}>{m.metodo}: <strong className={`tabular-nums ${corSinal(m.diferenca)}`}>{formatarMoeda(m.diferenca)}</strong></span>
            ))}
          </div>
        )}
      </Seccao>

      <Metodologia><strong>Metodologia:</strong> {r.metodologia}</Metodologia>
    </div>
  )
}
