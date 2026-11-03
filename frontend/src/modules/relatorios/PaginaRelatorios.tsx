import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Printer } from 'lucide-react'
import { useSessao } from '../../contexts/SessaoContext'
import { hojeIso, somarDias } from '../../utils/datas'
import { RelatorioFinanceiroVista } from './RelatorioFinanceiroVista'
import { RelatorioStockVista } from './RelatorioStockVista'
import { RelatorioVendasVista } from './RelatorioVendasVista'

type Periodo = 'hoje' | 'ontem' | '7dias' | 'mes' | 'mesAnterior' | 'personalizado'
type Tipo = 'vendas' | 'financeiro' | 'stock'

const TIPOS: { tipo: Tipo; titulo: string; permissao: string; nota: string }[] = [
  { tipo: 'vendas', titulo: 'Vendas', permissao: 'relatorios.vendas', nota: 'Vendas = pedidos pagos; estornos à parte.' },
  { tipo: 'financeiro', titulo: 'Financeiro e caixa', permissao: 'relatorios.financeiros', nota: 'Despesas pela data da despesa; fechos pela data de fecho.' },
  { tipo: 'stock', titulo: 'Stock e compras', permissao: 'relatorios.stock', nota: 'Movimentos pela data do movimento; o valor do stock é o actual.' },
]

function intervalo(p: Periodo, personalizado: { desde: string; ate: string }) {
  const hoje = hojeIso()
  const inicioMes = `${hoje.slice(0, 7)}-01`
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

export function PaginaRelatorios() {
  const { tem } = useSessao()
  const [parametros, setParametros] = useSearchParams()
  const [periodo, setPeriodo] = useState<Periodo>('7dias')
  const [personalizado, setPersonalizado] = useState({ desde: somarDias(hojeIso(), -30), ate: hojeIso() })
  const { desde, ate } = intervalo(periodo, personalizado)

  const disponiveis = TIPOS.filter((t) => tem(t.permissao))
  const actual = disponiveis.find((t) => t.tipo === parametros.get('tipo')) ?? disponiveis[0]

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">Relatório {actual ? `— ${actual.titulo}` : ''}</h1>
        <button type="button" className="btn-secundario print:hidden" onClick={() => window.print()}>
          <Printer className="h-4 w-4" aria-hidden /> Imprimir / PDF
        </button>
      </div>

      {disponiveis.length > 1 && (
        <div role="tablist" aria-label="Tipo de relatório" className="flex flex-wrap gap-1 border-b border-slate-200 print:hidden">
          {disponiveis.map((t) => (
            <button key={t.tipo} type="button" role="tab" aria-selected={t.tipo === actual?.tipo}
              onClick={() => setParametros({ tipo: t.tipo }, { replace: true })}
              className={`-mb-px min-h-11 border-b-2 px-4 text-sm font-medium ${t.tipo === actual?.tipo ? 'border-primaria-600 text-primaria-700' : 'border-transparent text-slate-600 hover:text-slate-900'}`}>
              {t.titulo}
            </button>
          ))}
        </div>
      )}

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
      <p className="text-sm text-slate-600">Período: {desde === ate ? desde : `${desde} a ${ate}`} (hora de Maputo). {actual?.nota}</p>

      {actual?.tipo === 'vendas' && <RelatorioVendasVista desde={desde} ate={ate} />}
      {actual?.tipo === 'financeiro' && <RelatorioFinanceiroVista desde={desde} ate={ate} />}
      {actual?.tipo === 'stock' && <RelatorioStockVista desde={desde} ate={ate} />}
    </div>
  )
}
