import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, ChefHat, ClipboardList, PackageX, RefreshCw, Wallet } from 'lucide-react'
import { api } from '../api/cliente'
import { BarrasHorizontais, GraficoBarras } from '../components/Graficos'
import { Alerta, Carregando } from '../components/ui'
import { useSessao } from '../contexts/SessaoContext'
import { useEvento } from '../contexts/TempoRealContext'
import { useDados } from '../hooks/useDados'
import type { Dashboard } from '../types/api'
import { formatarDataHora, formatarMoeda } from '../utils/formatar'

const diaSemana = new Intl.DateTimeFormat('pt-MZ', { weekday: 'short', day: '2-digit', timeZone: 'UTC' })
const n3 = new Intl.NumberFormat('pt-MZ', { maximumFractionDigits: 3 })

function Indicador({ rotulo, valor, nota, icone }: { rotulo: string; valor: string; nota?: ReactNode; icone?: ReactNode }) {
  return (
    <div className="cartao p-4">
      <div className="flex items-center justify-between text-sm text-slate-500">
        <span>{rotulo}</span>
        {icone}
      </div>
      <p className="mt-1 text-2xl font-semibold tabular-nums text-slate-900">{valor}</p>
      {nota && <p className="mt-1 text-xs text-slate-500">{nota}</p>}
    </div>
  )
}

export function Inicio() {
  const { utilizador, tem } = useSessao()
  if (!tem('dashboard.ver'))
    return (
      <div className="mx-auto max-w-3xl space-y-4">
        <h1 className="text-2xl font-semibold">Bem-vindo, {utilizador?.nomeCompleto}</h1>
        <Alerta tipo="info">Use o menu à esquerda para aceder às áreas do seu perfil.</Alerta>
      </div>
    )
  return <PainelDashboard />
}

function PainelDashboard() {
  const { tem } = useSessao()
  const dash = useDados(() => api.get<Dashboard>('/api/dashboard'), [])
  const [agora, setAgora] = useState(() => Date.now())

  // Actualiza com as vendas e a cozinha em tempo real (com um pequeno atraso para agrupar eventos).
  useEffect(() => {
    const id = window.setInterval(() => setAgora(Date.now()), 60_000)
    return () => window.clearInterval(id)
  }, [])
  const temporizador = useRef<number | undefined>(undefined)
  useEvento('pedidoAlterado', () => {
    window.clearTimeout(temporizador.current)
    temporizador.current = window.setTimeout(() => void dash.recarregar(), 1500)
  })
  useEffect(() => () => window.clearTimeout(temporizador.current), [])

  if (dash.erro) return <Alerta tipo="erro">{dash.erro}</Alerta>
  if (!dash.dados) return <Carregando />
  const d = dash.dados
  const variacao = d.vendasOntem > 0 ? ((d.vendasHoje - d.vendasOntem) / d.vendasOntem) * 100 : null
  const esperaCozinha = d.envioMaisAntigoPendenteUtc ? Math.max(0, Math.floor((agora - new Date(d.envioMaisAntigoPendenteUtc).getTime()) / 60000)) : null

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">Dashboard</h1>
        <button type="button" className="btn-secundario" onClick={() => void dash.recarregar()} disabled={dash.aCarregar}>
          <RefreshCw className={`h-4 w-4 ${dash.aCarregar ? 'animate-spin' : ''}`} aria-hidden /> Actualizar
        </button>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Indicador rotulo="Vendas de hoje" valor={formatarMoeda(d.vendasHoje)}
          nota={variacao === null ? 'Sem vendas ontem para comparar' : `${variacao >= 0 ? '+' : ''}${variacao.toFixed(0)}% face a ontem (${formatarMoeda(d.vendasOntem)})`} />
        <Indicador rotulo="Pedidos pagos hoje" valor={String(d.pedidosHoje)} nota={`Ticket médio ${formatarMoeda(d.ticketMedioHoje)}`} />
        <Indicador rotulo="Pedidos em aberto" valor={String(d.pedidosAbertos)} nota={`${formatarMoeda(d.valorEmAberto)} por receber`}
          icone={<ClipboardList className="h-4 w-4" aria-hidden />} />
        <Indicador rotulo="Cozinha" valor={`${d.itensCozinhaPendentes + d.itensCozinhaEmPreparacao} itens`}
          nota={`${d.itensCozinhaPendentes} pendentes · ${d.itensCozinhaEmPreparacao} em preparação${esperaCozinha !== null ? ` · mais antigo há ${esperaCozinha} min` : ''}`}
          icone={<ChefHat className={`h-4 w-4 ${esperaCozinha !== null && esperaCozinha >= 20 ? 'text-red-600' : ''}`} aria-hidden />} />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <section className="cartao p-4 lg:col-span-2">
          <h2 className="font-semibold">Vendas dos últimos 7 dias (MT)</h2>
          <GraficoBarras titulo="Vendas dos últimos 7 dias"
            dados={d.ultimos7Dias.map((v) => ({ rotulo: diaSemana.format(new Date(`${v.dia}T00:00:00Z`)), valor: v.total, detalhe: `${v.pedidos} pedidos` }))} />
        </section>
        <section className="cartao p-4">
          <h2 className="mb-3 font-semibold">Hoje por método de pagamento</h2>
          <BarrasHorizontais linhas={d.porMetodoHoje.map((m) => ({ rotulo: m.rotulo, valor: m.valor, extra: `(${m.quantidade})` }))} vazio="Ainda sem pagamentos hoje." />
        </section>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <section className="cartao p-4">
          <h2 className="mb-3 font-semibold">Produtos mais vendidos hoje</h2>
          <BarrasHorizontais linhas={d.topProdutosHoje.map((p) => ({ rotulo: p.produto, valor: p.total, extra: `× ${n3.format(p.quantidade)}` }))} vazio="Ainda sem vendas hoje." />
        </section>

        {d.caixas && (
          <section className="cartao p-4">
            <h2 className="mb-3 flex items-center gap-2 font-semibold"><Wallet className="h-4 w-4" aria-hidden /> Caixas abertos</h2>
            {d.caixas.length === 0 ? <p className="text-sm text-slate-500">Nenhum caixa aberto.</p> : (
              <ul className="divide-y divide-slate-100 text-sm">
                {d.caixas.map((c) => (
                  <li key={c.sessaoId} className="flex justify-between gap-2 py-2">
                    <span>
                      <span className="font-medium">{c.terminal}</span>
                      <span className="block text-xs text-slate-500">{c.operador} · desde {formatarDataHora(c.abertaEmUtc)}</span>
                    </span>
                    <span className="text-right font-medium tabular-nums">{formatarMoeda(c.totalEsperado)}<span className="block text-xs font-normal text-slate-500">esperado</span></span>
                  </li>
                ))}
              </ul>
            )}
            <Link to="/caixa" className="mt-2 inline-block text-sm font-medium text-primaria-700 hover:underline">Ir para o Caixa</Link>
          </section>
        )}

        <section className="cartao p-4">
          <h2 className="mb-3 flex items-center gap-2 font-semibold"><AlertTriangle className="h-4 w-4 text-amber-600" aria-hidden /> Alertas</h2>
          <ul className="space-y-2 text-sm">
            {d.cancelamentosHoje.pedidosCancelados > 0 && (
              <li>{d.cancelamentosHoje.pedidosCancelados} pedido(s) cancelado(s) hoje ({formatarMoeda(d.cancelamentosHoje.valorPedidosCancelados)})</li>
            )}
            {d.cancelamentosHoje.itensCancelados > 0 && (
              <li>{d.cancelamentosHoje.itensCancelados} item(ns) cancelado(s) depois de enviados ({formatarMoeda(d.cancelamentosHoje.valorItensCancelados)})</li>
            )}
            {d.cancelamentosHoje.pedidosEstornados > 0 && (
              <li>{d.cancelamentosHoje.pedidosEstornados} estorno(s) hoje ({formatarMoeda(d.cancelamentosHoje.valorEstornado)})</li>
            )}
            {d.stockBaixo?.map((s) => (
              <li key={s.artigoId} className="flex items-center gap-2">
                <PackageX className="h-4 w-4 shrink-0 text-red-600" aria-hidden />
                <span><strong>{s.artigo}</strong>: {n3.format(s.saldo)} {s.unidade} (mínimo {n3.format(s.minimo)})</span>
              </li>
            ))}
            {d.cancelamentosHoje.pedidosCancelados + d.cancelamentosHoje.itensCancelados + d.cancelamentosHoje.pedidosEstornados === 0 && !d.stockBaixo?.length && (
              <li className="text-slate-500">Sem alertas.</li>
            )}
          </ul>
          {tem('relatorios.vendas') && <Link to="/relatorios" className="mt-3 inline-block text-sm font-medium text-primaria-700 hover:underline">Ver relatórios</Link>}
        </section>
      </div>
    </div>
  )
}
