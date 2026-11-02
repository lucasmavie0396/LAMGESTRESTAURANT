import { useState, type ReactNode } from 'react'
import { Download, Info, Printer } from 'lucide-react'
import { api } from '../../api/cliente'
import { BarrasHorizontais, GraficoBarras } from '../../components/Graficos'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { RelatorioVendas } from '../../types/api'
import { descarregarCsv } from '../../utils/csv'
import { formatarMoeda } from '../../utils/formatar'

type Periodo = 'hoje' | 'ontem' | '7dias' | 'mes' | 'mesAnterior' | 'personalizado'

const n3 = new Intl.NumberFormat('pt-MZ', { maximumFractionDigits: 3 })
const dataCurta = new Intl.DateTimeFormat('pt-MZ', { day: '2-digit', month: '2-digit', timeZone: 'UTC' })

/** Data de hoje em Maputo no formato AAAA-MM-DD (o servidor interpreta as datas no fuso configurado). */
function hojeIso() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Maputo' }).format(new Date())
}
function somarDias(iso: string, dias: number) {
  const d = new Date(`${iso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + dias)
  return d.toISOString().slice(0, 10)
}
function intervalo(p: Periodo, personalizado: { desde: string; ate: string }) {
  const hoje = hojeIso()
  const [a, m] = hoje.split('-').map(Number)
  const inicioMes = `${a}-${String(m).padStart(2, '0')}-01`
  switch (p) {
    case 'hoje': return { desde: hoje, ate: hoje }
    case 'ontem': return { desde: somarDias(hoje, -1), ate: somarDias(hoje, -1) }
    case '7dias': return { desde: somarDias(hoje, -6), ate: hoje }
    case 'mes': return { desde: inicioMes, ate: hoje }
    case 'mesAnterior': {
      const fim = somarDias(inicioMes, -1)
      return { desde: `${fim.slice(0, 7)}-01`, ate: fim }
    }
    default: return personalizado
  }
}

function Seccao({ titulo, children, csv }: { titulo: string; children: ReactNode; csv?: () => void }) {
  return (
    <section className="cartao break-inside-avoid p-4">
      <div className="mb-3 flex items-center justify-between gap-2">
        <h2 className="font-semibold">{titulo}</h2>
        {csv && (
          <button type="button" className="btn-icone h-9 w-9 print:hidden" onClick={csv} title="Exportar CSV (Excel)" aria-label={`Exportar ${titulo} em CSV`}>
            <Download className="h-4 w-4" />
          </button>
        )}
      </div>
      {children}
    </section>
  )
}

export function PaginaRelatorios() {
  const [periodo, setPeriodo] = useState<Periodo>('7dias')
  const [personalizado, setPersonalizado] = useState({ desde: somarDias(hojeIso(), -30), ate: hojeIso() })
  const { desde, ate } = intervalo(periodo, personalizado)
  const rel = useDados(() => api.get<RelatorioVendas>(`/api/relatorios/vendas?desde=${desde}&ate=${ate}`), [desde, ate])
  const sufixo = `${desde}_${ate}`

  const r = rel.dados
  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">Relatório de vendas</h1>
        <button type="button" className="btn-secundario print:hidden" onClick={() => window.print()} disabled={!r}>
          <Printer className="h-4 w-4" aria-hidden /> Imprimir / PDF
        </button>
      </div>

      <div className="flex flex-wrap items-end gap-2 print:hidden">
        {([['hoje', 'Hoje'], ['ontem', 'Ontem'], ['7dias', 'Últimos 7 dias'], ['mes', 'Este mês'], ['mesAnterior', 'Mês anterior'], ['personalizado', 'Personalizado']] as const).map(([v, t]) => (
          <button key={v} type="button" onClick={() => setPeriodo(v)}
            className={`min-h-11 rounded-full px-4 text-sm font-medium ${periodo === v ? 'bg-primaria-600 text-white' : 'bg-white ring-1 ring-slate-300'}`}>{t}</button>
        ))}
        {periodo === 'personalizado' && (
          <div className="flex items-end gap-2">
            <label className="text-sm">De<input type="date" className="campo" value={personalizado.desde} max={personalizado.ate}
              onChange={(e) => setPersonalizado((p) => ({ ...p, desde: e.target.value }))} /></label>
            <label className="text-sm">Até<input type="date" className="campo" value={personalizado.ate} min={personalizado.desde}
              onChange={(e) => setPersonalizado((p) => ({ ...p, ate: e.target.value }))} /></label>
          </div>
        )}
      </div>
      <p className="text-sm text-slate-600">Período: {desde === ate ? desde : `${desde} a ${ate}`} (hora de Maputo). Vendas = pedidos pagos; estornos à parte.</p>

      {rel.erro && <Alerta tipo="erro">{rel.erro}</Alerta>}
      {!r && !rel.erro && <Carregando />}
      {r && (
        <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {[
              ['Total de vendas', formatarMoeda(r.totalVendas), `${r.numeroPedidos} pedidos`],
              ['Ticket médio', formatarMoeda(r.ticketMedio), `Descontos: ${formatarMoeda(r.descontos)}`],
              ['Impostos incluídos', formatarMoeda(r.impostos), `Taxas de entrega: ${formatarMoeda(r.taxasEntrega)}`],
              ['Margem estimada', formatarMoeda(r.margemEstimada), r.margemPercentagem === null ? '—' : `${r.margemPercentagem.toLocaleString('pt-MZ')}% da receita líquida`],
            ].map(([rotulo, valor, nota]) => (
              <div key={rotulo} className="cartao p-4">
                <p className="text-sm text-slate-500">{rotulo}</p>
                <p className="mt-1 text-2xl font-semibold tabular-nums">{valor}</p>
                <p className="mt-1 text-xs text-slate-500">{nota}</p>
              </div>
            ))}
          </div>

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

          <div className="flex gap-2 rounded-lg bg-slate-50 p-3 text-xs text-slate-600">
            <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <p><strong>Metodologia da margem:</strong> {r.metodologiaMargem} Receita líquida no período: {formatarMoeda(r.receitaLiquida)}; custo estimado: {formatarMoeda(r.custoEstimado)}.</p>
          </div>
        </div>
      )}
    </div>
  )
}
