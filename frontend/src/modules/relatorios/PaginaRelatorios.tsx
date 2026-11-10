import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { FileDown, Printer } from 'lucide-react'
import { api } from '../../api/cliente'
import { DialogoImprimir } from '../../components/DialogoImprimir'
import { useSessao } from '../../contexts/SessaoContext'
import type { Cliente, TipoDocumentoRelatorio } from '../../types/api'
import { hojeIso, somarDias } from '../../utils/datas'
import { SeletorCliente } from '../facturacao/FormDocumento'
import { RelatorioAnulacoesVista } from './RelatorioAnulacoesVista'
import { RelatorioCartoesVista } from './RelatorioCartoesVista'
import { RelatorioClientesVista } from './RelatorioClientesVista'
import { VISTAS_CONTAS, type VistaContas } from './contas'
import { ExtratoVista, FacturasCreditoVista, HistoricoClienteVista } from './RelatorioContasVista'
import { RelatorioFinanceiroVista } from './RelatorioFinanceiroVista'
import { RelatorioImpostosVista } from './RelatorioImpostosVista'
import { RelatorioListaVendasVista } from './RelatorioListaVendasVista'
import { RelatorioStockVista } from './RelatorioStockVista'
import { RelatorioVendasVista } from './RelatorioVendasVista'
import { intervalo, type Periodo } from './formato'

type Tipo = 'vendas' | 'lista' | 'clientes' | 'contas' | 'impostos' | 'anulacoes' | 'financeiro' | 'cartoes' | 'stock'
type ClienteEscolhido = Pick<Cliente, 'id' | 'nome' | 'nuit'>

const VENDAS = ['relatorios.vendas']
const FINANCEIROS = ['relatorios.financeiros']
const STOCK = ['relatorios.stock']
/** Contas correntes: quem consulta a facturação ou os relatórios de vendas/financeiros. */
const CONTAS = ['facturacao.ver', 'relatorios.vendas', 'relatorios.financeiros']

const TIPOS: { tipo: Tipo; titulo: string; permissoes: string[]; nota: string }[] = [
  { tipo: 'vendas', titulo: 'Vendas', permissoes: VENDAS, nota: 'Vendas = pedidos pagos; estornos à parte.' },
  { tipo: 'lista', titulo: 'Lista de vendas', permissoes: VENDAS, nota: 'Venda a venda, com filtros.' },
  { tipo: 'clientes', titulo: 'Clientes', permissoes: VENDAS, nota: 'Só as vendas com cliente associado no POS.' },
  { tipo: 'contas', titulo: 'Contas correntes', permissoes: CONTAS, nota: 'Facturas a crédito, recibos e notas de crédito pela data de emissão.' },
  { tipo: 'impostos', titulo: 'IVA e descontos', permissoes: VENDAS, nota: 'Imposto incluído nas vendas pagas, por taxa.' },
  { tipo: 'anulacoes', titulo: 'Anulações', permissoes: VENDAS, nota: 'Cancelamentos, estornos e pagamentos anulados (não são facturação).' },
  { tipo: 'financeiro', titulo: 'Financeiro e caixa', permissoes: FINANCEIROS, nota: 'Despesas pela data da despesa; fechos pela data de fecho.' },
  { tipo: 'cartoes', titulo: 'Cartões Evento', permissoes: FINANCEIROS, nota: 'Movimentos pela data; os saldos são os de hoje.' },
  { tipo: 'stock', titulo: 'Stock e compras', permissoes: STOCK, nota: 'Movimentos pela data do movimento; o valor do stock é o actual.' },
]

/** Documentos imprimíveis (talão 80/58 mm ou A4) de cada separador. */
const DOCUMENTOS: [Tipo, TipoDocumentoRelatorio, string[]][] = [
  ['vendas', 'Vendas', VENDAS], ['vendas', 'Pagamentos', VENDAS], ['vendas', 'Produtos', VENDAS],
  ['lista', 'ListaVendas', VENDAS], ['clientes', 'Clientes', VENDAS],
  ['contas', 'FacturasCredito', CONTAS], ['contas', 'HistoricoCliente', CONTAS], ['contas', 'ExtratoCliente', CONTAS],
  ['impostos', 'Impostos', VENDAS], ['anulacoes', 'Anulacoes', VENDAS],
  ['financeiro', 'Financeiro', FINANCEIROS], ['financeiro', 'Despesas', FINANCEIROS],
  ['cartoes', 'CartoesEvento', FINANCEIROS],
  ['stock', 'Stock', STOCK],
]

const DOCUMENTO_DA_VISTA: Record<VistaContas, TipoDocumentoRelatorio> = { facturas: 'FacturasCredito', historico: 'HistoricoCliente', extrato: 'ExtratoCliente' }

export function PaginaRelatorios() {
  const { tem } = useSessao()
  const temAlguma = (p: string[]) => p.some(tem)
  const [parametros, setParametros] = useSearchParams()
  const [periodo, setPeriodo] = useState<Periodo>('7dias')
  const [personalizado, setPersonalizado] = useState({ desde: somarDias(hojeIso(), -30), ate: hojeIso() })
  const { desde, ate } = intervalo(periodo, personalizado)

  const disponiveis = TIPOS.filter((t) => temAlguma(t.permissoes))
  const actual = disponiveis.find((t) => t.tipo === parametros.get('tipo')) ?? disponiveis[0]
  const vista: VistaContas = VISTAS_CONTAS.find((v) => v.vista === parametros.get('vista'))?.vista ?? 'facturas'
  const [imprimir, setImprimir] = useState(false)

  // Cliente das contas correntes: escolhido aqui ou vindo da ficha do cliente (?cliente=ID).
  const [cliente, setCliente] = useState<ClienteEscolhido | null>(null)
  const clienteUrl = Number(parametros.get('cliente')) || null
  useEffect(() => {
    if (clienteUrl && cliente?.id !== clienteUrl)
      api.get<Cliente>(`/api/clientes/${clienteUrl}`).then(setCliente).catch(() => setCliente(null))
  }, [clienteUrl]) // eslint-disable-line react-hooks/exhaustive-deps
  const mudarParametros = (alteracoes: Record<string, string | null>) => {
    const novos = new URLSearchParams(parametros)
    Object.entries(alteracoes).forEach(([k, v]) => (v === null ? novos.delete(k) : novos.set(k, v)))
    setParametros(novos, { replace: true })
  }
  const escolherCliente = (c: ClienteEscolhido | null) => {
    setCliente(c)
    mudarParametros({ cliente: c ? String(c.id) : null })
  }

  const documentos = DOCUMENTOS
    .filter(([, , p]) => temAlguma(p))
    .sort((a, b) => Number(b[0] === actual?.tipo) - Number(a[0] === actual?.tipo))
    .map(([, d]) => d)
  const tipoInicial = actual?.tipo === 'contas' ? DOCUMENTO_DA_VISTA[vista] : undefined
  const precisaCliente = actual?.tipo === 'contas' && VISTAS_CONTAS.some((v) => v.vista === vista && v.precisaCliente)
  // Relatório dos Cartões Evento só de um caixa (?caixa=ID); o documento impresso segue o mesmo filtro.
  const caixaCartoes = actual?.tipo === 'cartoes' ? Number(parametros.get('caixa')) || null : null

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
      {imprimir && (
        <DialogoImprimir tipos={[...documentos]} tipoInicial={tipoInicial} desde={desde} ate={ate}
          sessaoId={caixaCartoes ?? undefined} clienteId={cliente?.id} clienteNome={cliente?.nome} aoFechar={() => setImprimir(false)} />
      )}

      {disponiveis.length > 1 && (
        <div role="tablist" aria-label="Tipo de relatório" className="flex flex-wrap gap-1 border-b border-slate-200 print:hidden">
          {disponiveis.map((t) => (
            <button key={t.tipo} type="button" role="tab" aria-selected={t.tipo === actual?.tipo}
              onClick={() => setParametros(t.tipo === 'contas' && cliente ? { tipo: t.tipo, cliente: String(cliente.id) } : { tipo: t.tipo }, { replace: true })}
              className={`-mb-px min-h-11 border-b-2 px-4 text-sm font-medium ${t.tipo === actual?.tipo ? 'border-primaria-600 text-primaria-700' : 'border-transparent text-slate-600 hover:text-slate-900'}`}>
              {t.titulo}
            </button>
          ))}
        </div>
      )}

      {actual?.tipo === 'contas' && (
        <div className="flex flex-wrap items-end gap-3 print:hidden">
          <div role="radiogroup" aria-label="Relatório de contas correntes" className="flex flex-wrap gap-1 rounded-lg bg-slate-100 p-1">
            {VISTAS_CONTAS.map((v) => (
              <button key={v.vista} type="button" role="radio" aria-checked={v.vista === vista} onClick={() => mudarParametros({ vista: v.vista })}
                className={`min-h-10 rounded-md px-3 text-sm font-medium ${v.vista === vista ? 'bg-white text-primaria-700 shadow' : 'text-slate-600'}`}>
                {v.titulo}
              </button>
            ))}
          </div>
          <div className="w-full max-w-md">
            <SeletorCliente valor={cliente} aoMudar={escolherCliente} rotulo={precisaCliente ? 'Cliente' : 'Cliente (opcional: vazio = todos)'} />
          </div>
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
      {actual?.tipo === 'contas' && vista === 'facturas' && <FacturasCreditoVista desde={desde} ate={ate} clienteId={cliente?.id} />}
      {precisaCliente && !cliente && (
        <p className="rounded-lg bg-slate-50 p-4 text-sm text-slate-600">Escolha o cliente para ver o {vista === 'extrato' ? 'extrato' : 'histórico'}.</p>
      )}
      {actual?.tipo === 'contas' && vista === 'historico' && cliente && <HistoricoClienteVista clienteId={cliente.id} desde={desde} ate={ate} />}
      {actual?.tipo === 'contas' && vista === 'extrato' && cliente && <ExtratoVista clienteId={cliente.id} desde={desde} ate={ate} />}
      {actual?.tipo === 'impostos' && <RelatorioImpostosVista desde={desde} ate={ate} />}
      {actual?.tipo === 'anulacoes' && <RelatorioAnulacoesVista desde={desde} ate={ate} />}
      {actual?.tipo === 'financeiro' && <RelatorioFinanceiroVista desde={desde} ate={ate} />}
      {actual?.tipo === 'cartoes' && <RelatorioCartoesVista desde={desde} ate={ate} sessao={caixaCartoes} aoMudarSessao={(s) => mudarParametros({ caixa: s === null ? null : String(s) })} />}
      {actual?.tipo === 'stock' && <RelatorioStockVista desde={desde} ate={ate} />}
    </div>
  )
}
