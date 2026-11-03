import { useState } from 'react'
import { api } from '../../api/cliente'
import { BarrasHorizontais, GraficoBarras } from '../../components/Graficos'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { RelatorioVendas } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { Indicadores, Metodologia, Seccao } from './comum'
import { n3, percentagem, tabela } from './formato'

const dataCurta = new Intl.DateTimeFormat('pt-MZ', { day: '2-digit', month: '2-digit', timeZone: 'UTC' })

export function RelatorioVendasVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useDados(() => api.get<RelatorioVendas>(`/api/relatorios/vendas?desde=${desde}&ate=${ate}`), [desde, ate])
  const [ordemProdutos, setOrdemProdutos] = useState<'total' | 'quantidade'>('total')
  const sufixo = `${desde}_${ate}`

  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const recebido = r.porMetodo.reduce((s, m) => s + m.valor, 0)
  const totalCategorias = r.porCategoria.reduce((s, c) => s + c.total, 0)
  const produtos = [...r.produtos].sort((a, b) => (ordemProdutos === 'total' ? b.total - a.total : b.quantidade - a.quantidade) || a.produto.localeCompare(b.produto))
  const semVendas = r.produtosSemVendas ?? []
  const operadores = r.operadores ?? []
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Total de vendas', formatarMoeda(r.totalVendas), `${r.numeroPedidos} pedidos`],
        ['Ticket médio', formatarMoeda(r.ticketMedio), `Descontos: ${formatarMoeda(r.descontos)}`],
        ['Impostos incluídos', formatarMoeda(r.impostos), `Taxas de entrega: ${formatarMoeda(r.taxasEntrega)}`],
        ['Margem estimada', formatarMoeda(r.margemEstimada), r.margemPercentagem === null ? '—' : `${r.margemPercentagem.toLocaleString('pt-MZ')}% da receita líquida`],
      ]} />

      <Seccao titulo="Vendas por dia (MT)" exportar={() => tabela(`vendas-por-dia_${sufixo}`, ['Dia', 'Pedidos', 'Total (MT)'], r.porDia.map((d) => [d.dia, d.pedidos, d.total]))}>
        {r.porDia.length > 62 ? <p className="text-sm text-slate-500">Período longo: use a exportação para o detalhe diário.</p> : (
          <GraficoBarras titulo="Vendas por dia" altura={260}
            dados={r.porDia.map((d) => ({ rotulo: dataCurta.format(new Date(`${d.dia}T00:00:00Z`)), valor: d.total, detalhe: `${d.pedidos} pedidos` }))} />
        )}
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-3">
        <Seccao titulo="Métodos de pagamento" exportar={() => tabela(`metodos_${sufixo}`, ['Método', 'Pagamentos', 'Total (MT)', '% do recebido'],
          r.porMetodo.map((m) => [m.rotulo, m.quantidade, m.valor, percentagem(m.valor, recebido)]))}>
          <BarrasHorizontais linhas={r.porMetodo.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade}) · ${percentagem(m.valor, recebido)}` }))} />
        </Seccao>
        <Seccao titulo="Tipo de pedido" exportar={() => tabela(`tipos_${sufixo}`, ['Tipo', 'Pedidos', 'Total (MT)'], r.porTipo.map((m) => [m.rotulo, m.quantidade, m.valor]))}>
          <BarrasHorizontais linhas={r.porTipo.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} />
        </Seccao>
        <Seccao titulo="Categorias" exportar={() => tabela(`categorias_${sufixo}`, ['Categoria', 'Quantidade', 'Total (MT)', '% das vendas'],
          r.porCategoria.map((c) => [c.categoria, c.quantidade, c.total, percentagem(c.total, totalCategorias)]))}>
          <BarrasHorizontais linhas={r.porCategoria.map((c) => ({ rotulo: c.categoria, valor: c.total, extra: percentagem(c.total, totalCategorias) }))} />
        </Seccao>
      </div>

      <Seccao titulo={ordemProdutos === 'total' ? 'Produtos (por facturação)' : 'Produtos (por quantidade)'}
        accoes={
          <div className="flex rounded-lg ring-1 ring-slate-300 print:hidden" role="group" aria-label="Ordenar produtos">
            {([['total', 'Facturação'], ['quantidade', 'Quantidade']] as const).map(([v, t]) => (
              <button key={v} type="button" onClick={() => setOrdemProdutos(v)} aria-pressed={ordemProdutos === v}
                className={`min-h-9 px-3 text-xs font-medium first:rounded-l-lg last:rounded-r-lg ${ordemProdutos === v ? 'bg-primaria-600 text-white' : 'bg-white'}`}>{t}</button>
            ))}
          </div>
        }
        exportar={() => tabela(`produtos_${sufixo}`, ['Código', 'Produto', 'Categoria', 'Quantidade', 'Preço médio (MT)', 'Total (MT)', 'Custo estimado (MT)', 'Margem estimada (MT)'],
          produtos.map((p) => [p.codigo, p.produto, p.categoria, p.quantidade, p.quantidade ? Math.round((p.total / p.quantidade) * 100) / 100 : 0,
            p.total, p.custoEstimado, Math.round((p.total - p.custoEstimado) * 100) / 100]))}>
        {produtos.length === 0 ? <Vazio texto="Sem vendas no período." /> : (
          <div className="max-h-96 overflow-auto print:max-h-none">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Código</th>
                  <th scope="col" className="py-2">Produto</th>
                  <th scope="col" className="py-2">Categoria</th>
                  <th scope="col" className="py-2 text-right">Qtd.</th>
                  <th scope="col" className="py-2 text-right">Preço médio</th>
                  <th scope="col" className="py-2 text-right">Total</th>
                  <th scope="col" className="py-2 text-right">Custo est.</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {produtos.map((p) => (
                  <tr key={p.produtoId}>
                    <td className="py-1.5 text-slate-500">{p.codigo}</td>
                    <td className="py-1.5 font-medium">{p.produto}</td>
                    <td className="py-1.5 text-slate-600">{p.categoria}</td>
                    <td className="py-1.5 text-right">{n3.format(p.quantidade)}</td>
                    <td className="py-1.5 text-right text-slate-600">{formatarMoeda(p.quantidade ? p.total / p.quantidade : 0)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(p.total)}</td>
                    <td className="py-1.5 text-right text-slate-600">{formatarMoeda(p.custoEstimado)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>

      <Seccao titulo={`Produtos sem vendas no período (${semVendas.length})`}
        exportar={() => tabela(`produtos-sem-vendas_${sufixo}`, ['Código', 'Produto', 'Categoria'], semVendas.map((p) => [p.codigo, p.produto, p.categoria]))}>
        {semVendas.length === 0 ? <p className="text-sm text-slate-500">Todos os produtos activos tiveram vendas.</p> : (
          <ul className="grid max-h-60 gap-x-4 gap-y-1 overflow-auto text-sm sm:grid-cols-2 lg:grid-cols-3 print:max-h-none">
            {semVendas.map((p) => <li key={p.produtoId}><span className="text-slate-500">{p.codigo}</span> {p.produto} <span className="text-xs text-slate-500">· {p.categoria}</span></li>)}
          </ul>
        )}
      </Seccao>

      <Seccao titulo="Por operador (quem registou o pedido)"
        exportar={() => tabela(`operadores_${sufixo}`, ['Operador', 'Vendas', 'Total (MT)', 'Descontos (MT)', 'Métodos'],
          operadores.map((o) => [o.operador, o.vendas, o.total, o.descontos, o.metodos.map((m) => `${m.rotulo} ${m.valor.toFixed(2)}`).join('; ')]))}>
        {operadores.length === 0 ? <Vazio texto="Sem vendas no período." /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Operador</th>
                  <th scope="col" className="py-2 text-right">Vendas</th>
                  <th scope="col" className="py-2 text-right">Total</th>
                  <th scope="col" className="py-2 text-right">Descontos</th>
                  <th scope="col" className="py-2 pl-4">Formas de pagamento</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {operadores.map((o) => (
                  <tr key={o.operador}>
                    <td className="py-1.5 font-medium">{o.operador}</td>
                    <td className="py-1.5 text-right">{o.vendas}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(o.total)}</td>
                    <td className="py-1.5 text-right text-slate-600">{formatarMoeda(o.descontos)}</td>
                    <td className="py-1.5 pl-4 text-xs text-slate-600">{o.metodos.map((m) => `${m.rotulo} ${formatarMoeda(m.valor)}`).join(' · ')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-2">
        <Seccao titulo="Por garçom">
          <BarrasHorizontais linhas={r.porGarcom.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))}
            vazio="Sem pedidos associados a garçons (associe utilizadores a funcionários)." />
        </Seccao>
        <Seccao titulo="Cancelamentos e estornos">
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt>Pedidos cancelados</dt><dd className="tabular-nums">{r.cancelamentos.pedidosCancelados} · {formatarMoeda(r.cancelamentos.valorPedidosCancelados)}</dd></div>
            <div className="flex justify-between"><dt>Itens cancelados após envio</dt><dd className="tabular-nums">{r.cancelamentos.itensCancelados} · {formatarMoeda(r.cancelamentos.valorItensCancelados)}</dd></div>
            <div className="flex justify-between"><dt>Pedidos estornados</dt><dd className="tabular-nums">{r.cancelamentos.pedidosEstornados} · {formatarMoeda(r.cancelamentos.valorEstornado)}</dd></div>
          </dl>
          <p className="mt-2 text-xs text-slate-500">O detalhe (com motivos) está no separador «Anulações».</p>
        </Seccao>
      </div>

      <Metodologia>
        <strong>Metodologia da margem:</strong> {r.metodologiaMargem} Receita líquida no período: {formatarMoeda(r.receitaLiquida)}; custo estimado: {formatarMoeda(r.custoEstimado)}.
      </Metodologia>
    </div>
  )
}
