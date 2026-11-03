import { api } from '../../api/cliente'
import { BarrasHorizontais, GraficoBarras } from '../../components/Graficos'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { RelatorioVendas } from '../../types/api'
import { descarregarCsv } from '../../utils/csv'
import { formatarMoeda } from '../../utils/formatar'
import { Indicadores, Metodologia, Seccao } from './comum'
import { n3 } from './formato'

const dataCurta = new Intl.DateTimeFormat('pt-MZ', { day: '2-digit', month: '2-digit', timeZone: 'UTC' })

export function RelatorioVendasVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useDados(() => api.get<RelatorioVendas>(`/api/relatorios/vendas?desde=${desde}&ate=${ate}`), [desde, ate])
  const sufixo = `${desde}_${ate}`

  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Total de vendas', formatarMoeda(r.totalVendas), `${r.numeroPedidos} pedidos`],
        ['Ticket médio', formatarMoeda(r.ticketMedio), `Descontos: ${formatarMoeda(r.descontos)}`],
        ['Impostos incluídos', formatarMoeda(r.impostos), `Taxas de entrega: ${formatarMoeda(r.taxasEntrega)}`],
        ['Margem estimada', formatarMoeda(r.margemEstimada), r.margemPercentagem === null ? '—' : `${r.margemPercentagem.toLocaleString('pt-MZ')}% da receita líquida`],
      ]} />

      <Seccao titulo="Vendas por dia (MT)" csv={() => descarregarCsv(`vendas-por-dia_${sufixo}`, ['Dia', 'Pedidos', 'Total (MT)'], r.porDia.map((d) => [d.dia, d.pedidos, d.total]))}>
        {r.porDia.length > 62 ? <p className="text-sm text-slate-500">Período longo: use a exportação CSV para o detalhe diário.</p> : (
          <GraficoBarras titulo="Vendas por dia" altura={260}
            dados={r.porDia.map((d) => ({ rotulo: dataCurta.format(new Date(`${d.dia}T00:00:00Z`)), valor: d.total, detalhe: `${d.pedidos} pedidos` }))} />
        )}
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-3">
        <Seccao titulo="Métodos de pagamento" csv={() => descarregarCsv(`metodos_${sufixo}`, ['Método', 'Pagamentos', 'Total (MT)'], r.porMetodo.map((m) => [m.rotulo, m.quantidade, m.valor]))}>
          <BarrasHorizontais linhas={r.porMetodo.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} />
        </Seccao>
        <Seccao titulo="Tipo de pedido" csv={() => descarregarCsv(`tipos_${sufixo}`, ['Tipo', 'Pedidos', 'Total (MT)'], r.porTipo.map((m) => [m.rotulo, m.quantidade, m.valor]))}>
          <BarrasHorizontais linhas={r.porTipo.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} />
        </Seccao>
        <Seccao titulo="Categorias" csv={() => descarregarCsv(`categorias_${sufixo}`, ['Categoria', 'Quantidade', 'Total (MT)'], r.porCategoria.map((c) => [c.categoria, c.quantidade, c.total]))}>
          <BarrasHorizontais linhas={r.porCategoria.map((c) => ({ rotulo: c.categoria, valor: c.total }))} />
        </Seccao>
      </div>

      <Seccao titulo="Produtos (do mais ao menos vendido)"
        csv={() => descarregarCsv(`produtos_${sufixo}`, ['Produto', 'Categoria', 'Quantidade', 'Total (MT)', 'Custo estimado (MT)', 'Margem estimada (MT)'],
          r.produtos.map((p) => [p.produto, p.categoria, p.quantidade, p.total, p.custoEstimado, Math.round((p.total - p.custoEstimado) * 100) / 100]))}>
        {r.produtos.length === 0 ? <Vazio texto="Sem vendas no período." /> : (
          <div className="max-h-96 overflow-auto print:max-h-none">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Produto</th>
                  <th scope="col" className="py-2">Categoria</th>
                  <th scope="col" className="py-2 text-right">Qtd.</th>
                  <th scope="col" className="py-2 text-right">Total</th>
                  <th scope="col" className="py-2 text-right">Custo est.</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.produtos.map((p) => (
                  <tr key={p.produtoId}>
                    <td className="py-1.5 font-medium">{p.produto}</td>
                    <td className="py-1.5 text-slate-600">{p.categoria}</td>
                    <td className="py-1.5 text-right">{n3.format(p.quantidade)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(p.total)}</td>
                    <td className="py-1.5 text-right text-slate-600">{formatarMoeda(p.custoEstimado)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-3">
        <Seccao titulo="Por utilizador (quem registou o pedido)"
          csv={() => descarregarCsv(`utilizadores_${sufixo}`, ['Utilizador', 'Pedidos', 'Total (MT)'], r.porUtilizador.map((m) => [m.rotulo, m.quantidade, m.valor]))}>
          <BarrasHorizontais linhas={r.porUtilizador.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} />
        </Seccao>
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
        </Seccao>
      </div>

      <Metodologia>
        <strong>Metodologia da margem:</strong> {r.metodologiaMargem} Receita líquida no período: {formatarMoeda(r.receitaLiquida)}; custo estimado: {formatarMoeda(r.custoEstimado)}.
      </Metodologia>
    </div>
  )
}
