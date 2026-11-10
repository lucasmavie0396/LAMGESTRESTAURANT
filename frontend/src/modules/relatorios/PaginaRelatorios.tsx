import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { FileDown, Printer } from 'lucide-react'
import { DialogoImprimir } from '../../components/DialogoImprimir'
import { useSessao } from '../../contexts/SessaoContext'
import { hojeIso, somarDias } from '../../utils/datas'
import { RelatorioAnulacoesVista } from './RelatorioAnulacoesVista'
import { RelatorioCartoesVista } from './RelatorioCartoesVista'
import { RelatorioClientesVista } from './RelatorioClientesVista'
import { RelatorioFinanceiroVista } from './RelatorioFinanceiroVista'
import { RelatorioImpostosVista } from './RelatorioImpostosVista'
import { RelatorioListaVendasVista } from './RelatorioListaVendasVista'
import { RelatorioStockVista } from './RelatorioStockVista'
import { RelatorioVendasVista } from './RelatorioVendasVista'
import { intervalo, type Periodo } from './formato'

type Tipo = 'vendas' | 'lista' | 'clientes' | 'impostos' | 'anulacoes' | 'financeiro' | 'cartoes' | 'stock'

const TIPOS: { tipo: Tipo; titulo: string; permissao: string; nota: string }[] = [
  { tipo: 'vendas', titulo: 'Vendas', permissao: 'relatorios.vendas', nota: 'Vendas = pedidos pagos; estornos à parte.' },
  { tipo: 'lista', titulo: 'Lista de vendas', permissao: 'relatorios.vendas', nota: 'Venda a venda, com filtros.' },
  { tipo: 'clientes', titulo: 'Clientes', permissao: 'relatorios.vendas', nota: 'Só as vendas com cliente associado no POS.' },
  { tipo: 'impostos', titulo: 'IVA e descontos', permissao: 'relatorios.vendas', nota: 'Imposto incluído nas vendas pagas, por taxa.' },
  { tipo: 'anulacoes', titulo: 'Anulações', permissao: 'relatorios.vendas', nota: 'Cancelamentos, estornos e pagamentos anulados (não são facturação).' },
  { tipo: 'financeiro', titulo: 'Financeiro e caixa', permissao: 'relatorios.financeiros', nota: 'Despesas pela data da despesa; fechos pela data de fecho.' },
  { tipo: 'cartoes', titulo: 'Cartões Evento', permissao: 'relatorios.financeiros', nota: 'Movimentos pela data; os saldos são os de hoje.' },
  { tipo: 'stock', titulo: 'Stock e compras', permissao: 'relatorios.stock', nota: 'Movimentos pela data do movimento; o valor do stock é o actual.' },
]

export function PaginaRelatorios() {
  const { tem } = useSessao()
  const [parametros, setParametros] = useSearchParams()
  const [periodo, setPeriodo] = useState<Periodo>('7dias')
  const [personalizado, setPersonalizado] = useState({ desde: somarDias(hojeIso(), -30), ate: hojeIso() })
  const { desde, ate } = intervalo(periodo, personalizado)

  const disponiveis = TIPOS.filter((t) => tem(t.permissao))
  const actual = disponiveis.find((t) => t.tipo === parametros.get('tipo')) ?? disponiveis[0]
  const [imprimir, setImprimir] = useState(false)
  // Documentos imprimíveis (talão 80/58 mm ou A4) conforme as permissões; o primeiro é o do separador aberto.
  const documentos = (
    [
      ['vendas', 'Vendas', 'relatorios.vendas'], ['vendas', 'Pagamentos', 'relatorios.vendas'], ['vendas', 'Produtos', 'relatorios.vendas'],
      ['lista', 'ListaVendas', 'relatorios.vendas'], ['clientes', 'Clientes', 'relatorios.vendas'],
      ['impostos', 'Impostos', 'relatorios.vendas'], ['anulacoes', 'Anulacoes', 'relatorios.vendas'],
      ['financeiro', 'Financeiro', 'relatorios.financeiros'], ['financeiro', 'Despesas', 'relatorios.financeiros'],
      ['cartoes', 'CartoesEvento', 'relatorios.financeiros'],
      ['stock', 'Stock', 'relatorios.stock'],
    ] as const
  ).filter(([, , p]) => tem(p)).sort((a, b) => Number(b[0] === actual?.tipo) - Number(a[0] === actual?.tipo)).map(([, d]) => d)

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">Relatório {actual ? `— ${actual.titulo}` : ''}</h1>
        <div className="flex gap-2 print:hidden">
          {documentos.length > 0 && (
            <button type="button" className="btn-primario" onClick={() => setImprimir(true)}>
              <Printer className="h-4 w-4" aria-hidden /> Imprimir documento
            </button>
          )}
          <button type="button" className="btn-secundario" onClick={() => window.print()} title="Imprime este ecrã com os gráficos (A4)">
            <FileDown className="h-4 w-4" aria-hidden /> Ecrã / PDF
          </button>
        </div>
      </div>
      {imprimir && <DialogoImprimir tipos={[...documentos]} desde={desde} ate={ate} aoFechar={() => setImprimir(false)} />}

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
        {([['hoje', 'Hoje'], ['ontem', 'Ontem'], ['semana', 'Esta semana'], ['7dias', 'Últimos 7 dias'], ['mes', 'Este mês'], ['mesAnterior', 'Mês anterior'],
          ['ano', 'Este ano'], ['personalizado', 'Personalizado']] as const).map(([v, t]) => (
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
      {actual?.tipo === 'lista' && <RelatorioListaVendasVista desde={desde} ate={ate} />}
      {actual?.tipo === 'clientes' && <RelatorioClientesVista desde={desde} ate={ate} />}
      {actual?.tipo === 'impostos' && <RelatorioImpostosVista desde={desde} ate={ate} />}
      {actual?.tipo === 'anulacoes' && <RelatorioAnulacoesVista desde={desde} ate={ate} />}
      {actual?.tipo === 'financeiro' && <RelatorioFinanceiroVista desde={desde} ate={ate} />}
      {actual?.tipo === 'cartoes' && <RelatorioCartoesVista desde={desde} ate={ate} />}
      {actual?.tipo === 'stock' && <RelatorioStockVista desde={desde} ate={ate} />}
    </div>
  )
}
