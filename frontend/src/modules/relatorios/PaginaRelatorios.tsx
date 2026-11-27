import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { FileDown, Printer } from 'lucide-react'
import { api } from '../../api/cliente'
import { DialogoImprimir } from '../../components/DialogoImprimir'
import { useSessao } from '../../contexts/SessaoContext'
import { useDados } from '../../hooks/useDados'
import type { Armazem, Cliente, TipoDocumentoRelatorio } from '../../types/api'
import { RelatorioAcompanhamentosVista, RelatorioExistenciasVista, RelatorioTransferenciasVista } from './RelatoriosArmazemVista'
import { hojeIso, somarDias } from '../../utils/datas'
import { SeletorCliente } from '../facturacao/FormDocumento'
import { RelatorioAnulacoesVista } from './RelatorioAnulacoesVista'
import { RelatorioCartoesVista } from './RelatorioCartoesVista'
import { ExtratoAdiantamentosVista, RelatorioAdiantamentosVista } from './RelatorioAdiantamentosVista'
import { ContasPagarVista, CotacoesVista, CozinhaVista, DeliveryVista, HorasVista, MesasVista, PontosVista, ReservasVista } from './RelatoriosOperacaoVista'
import { RelatorioClientesVista } from './RelatorioClientesVista'
import { VISTAS_CONTAS, type VistaContas } from './contas'
import { ExtratoVista, FacturasCreditoVista, HistoricoClienteVista } from './RelatorioContasVista'
import { RelatorioFinanceiroVista } from './RelatorioFinanceiroVista'
import { RelatorioImpostosVista } from './RelatorioImpostosVista'
import { RelatorioListaVendasVista } from './RelatorioListaVendasVista'
import { RelatorioStockVista } from './RelatorioStockVista'
import { RelatorioVendasVista } from './RelatorioVendasVista'
import { intervalo, type Periodo } from './formato'

type Tipo = 'vendas' | 'lista' | 'clientes' | 'contas' | 'impostos' | 'anulacoes' | 'financeiro' | 'cartoes' | 'adiantamentos' | 'contasPagar' | 'cotacoes' | 'operacao' | 'pontos' | 'stock' | 'acompanhamentos' | 'existencias' | 'transferencias'
/** Separadores com o filtro de armazém. */
const COM_ARMAZEM: Tipo[] = ['stock', 'existencias', 'transferencias']
type ClienteEscolhido = Pick<Cliente, 'id' | 'nome' | 'nuit'>

const VENDAS = ['relatorios.vendas']
const FINANCEIROS = ['relatorios.financeiros']
const STOCK = ['relatorios.stock']
/** Contas correntes: quem consulta a facturação ou os relatórios de vendas/financeiros. */
const CONTAS = ['facturacao.ver', 'relatorios.vendas', 'relatorios.financeiros']
/** Contas a pagar: financeiros ou quem consulta as compras. */
const FORNECEDORES = ['relatorios.financeiros', 'compras.ver']

/** Vistas do separador «Operação» (cada uma com o seu documento imprimível). */
type VistaOperacao = 'horas' | 'cozinha' | 'mesas' | 'delivery' | 'reservas'
const VISTAS_OPERACAO: { vista: VistaOperacao; titulo: string; documento: TipoDocumentoRelatorio }[] = [
  { vista: 'horas', titulo: 'Vendas por hora', documento: 'VendasPorHora' },
  { vista: 'cozinha', titulo: 'Tempos da cozinha', documento: 'Cozinha' },
  { vista: 'mesas', titulo: 'Mesas', documento: 'Mesas' },
  { vista: 'delivery', titulo: 'Delivery e Take Away', documento: 'Delivery' },
  { vista: 'reservas', titulo: 'Reservas', documento: 'Reservas' },
]

const TIPOS: { tipo: Tipo; titulo: string; permissoes: string[]; nota: string }[] = [
  { tipo: 'vendas', titulo: 'Vendas', permissoes: VENDAS, nota: 'Vendas = pedidos pagos; estornos à parte.' },
  { tipo: 'acompanhamentos', titulo: 'Acompanhamentos', permissoes: VENDAS, nota: 'Acompanhamentos escolhidos nos pratos pagos.' },
  { tipo: 'lista', titulo: 'Lista de vendas', permissoes: VENDAS, nota: 'Venda a venda, com filtros.' },
  { tipo: 'clientes', titulo: 'Clientes', permissoes: VENDAS, nota: 'Só as vendas com cliente associado no POS.' },
  { tipo: 'contas', titulo: 'Contas correntes', permissoes: CONTAS, nota: 'Facturas a crédito, recibos e notas de crédito pela data de emissão.' },
  { tipo: 'impostos', titulo: 'IVA e descontos', permissoes: VENDAS, nota: 'Imposto incluído nas vendas pagas, por taxa.' },
  { tipo: 'anulacoes', titulo: 'Anulações', permissoes: VENDAS, nota: 'Cancelamentos, estornos e pagamentos anulados (não são facturação).' },
  { tipo: 'financeiro', titulo: 'Financeiro e caixa', permissoes: FINANCEIROS, nota: 'Despesas pela data da despesa; fechos pela data de fecho.' },
  { tipo: 'cartoes', titulo: 'Cartões Evento', permissoes: FINANCEIROS, nota: 'Movimentos pela data; os saldos são os de hoje.' },
  { tipo: 'contasPagar', titulo: 'Contas a pagar', permissoes: FORNECEDORES, nota: 'Dívida aos fornecedores de hoje; pagamentos pela data.' },
  { tipo: 'cotacoes', titulo: 'Cotações', permissoes: CONTAS, nota: 'Cotações pela data de emissão; situação de hoje.' },
  { tipo: 'operacao', titulo: 'Operação', permissoes: VENDAS, nota: 'Horas de ponta, cozinha, mesas, delivery e reservas.' },
  { tipo: 'pontos', titulo: 'Pontos', permissoes: VENDAS, nota: 'Movimentos de pontos pela data; saldos de hoje.' },
  { tipo: 'adiantamentos', titulo: 'Adiantamentos', permissoes: FINANCEIROS, nota: 'Cartões de clientes: recargas e consumos pela data; saldos no início e no fim do período.' },
  { tipo: 'stock', titulo: 'Stock e compras', permissoes: STOCK, nota: 'Movimentos pela data do movimento; o valor do stock é o actual.' },
  { tipo: 'existencias', titulo: 'Existências por armazém', permissoes: STOCK, nota: 'Saldos de hoje (o período não se aplica).' },
  { tipo: 'transferencias', titulo: 'Transferências', permissoes: STOCK, nota: 'Transferências entre armazéns pela data.' },
]

/** Documentos imprimíveis (talão 80/58 mm ou A4) de cada separador. */
const DOCUMENTOS: [Tipo, TipoDocumentoRelatorio, string[]][] = [
  ['vendas', 'Vendas', VENDAS], ['vendas', 'Pagamentos', VENDAS], ['vendas', 'Produtos', VENDAS],
  ['lista', 'ListaVendas', VENDAS], ['clientes', 'Clientes', VENDAS],
  ['contas', 'FacturasCredito', CONTAS], ['contas', 'HistoricoCliente', CONTAS], ['contas', 'ExtratoCliente', CONTAS],
  ['impostos', 'Impostos', VENDAS], ['anulacoes', 'Anulacoes', VENDAS],
  ['financeiro', 'Financeiro', FINANCEIROS], ['financeiro', 'Despesas', FINANCEIROS],
  ['cartoes', 'CartoesEvento', FINANCEIROS], ['cartoes', 'DescargasCartoes', FINANCEIROS],
  ['adiantamentos', 'Adiantamentos', FINANCEIROS], ['adiantamentos', 'ExtratoAdiantamentos', FINANCEIROS],
  ['contasPagar', 'ContasPagar', FORNECEDORES], ['cotacoes', 'Cotacoes', CONTAS], ['pontos', 'Pontos', VENDAS],
  ...VISTAS_OPERACAO.map((v): [Tipo, TipoDocumentoRelatorio, string[]] => ['operacao', v.documento, VENDAS]),
  ['stock', 'Stock', STOCK], ['existencias', 'Existencias', STOCK], ['transferencias', 'Transferencias', STOCK],
  ['acompanhamentos', 'Acompanhamentos', VENDAS],
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
  const vistaOperacao = VISTAS_OPERACAO.find((v) => v.vista === parametros.get('vista'))?.vista ?? 'horas'
  const tipoInicial = actual?.tipo === 'contas' ? DOCUMENTO_DA_VISTA[vista]
    : actual?.tipo === 'operacao' ? VISTAS_OPERACAO.find((v) => v.vista === vistaOperacao)!.documento
    : actual?.tipo === 'adiantamentos' ? (cliente ? 'ExtratoAdiantamentos' : 'Adiantamentos') : undefined
  const precisaCliente = actual?.tipo === 'contas' && VISTAS_CONTAS.some((v) => v.vista === vista && v.precisaCliente)
  // Relatório dos Cartões Evento só de um caixa (?caixa=ID); o documento impresso segue o mesmo filtro.
  const caixaCartoes = actual?.tipo === 'cartoes' ? Number(parametros.get('caixa')) || null : null
  // Relatórios de stock só de um armazém (?armazem=ID); o documento impresso segue o mesmo filtro.
  const comArmazem = !!actual && COM_ARMAZEM.includes(actual.tipo)
  const armazemId = comArmazem ? Number(parametros.get('armazem')) || null : null
  const armazens = useDados(() => (temAlguma(STOCK) ? api.get<Armazem[]>('/api/stock/armazens') : Promise.resolve([] as Armazem[])), [])

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
          sessaoId={caixaCartoes ?? undefined} clienteId={cliente?.id} clienteNome={cliente?.nome} armazemId={armazemId ?? undefined} aoFechar={() => setImprimir(false)} />
      )}

      {disponiveis.length > 1 && (
        <div role="tablist" aria-label="Tipo de relatório" className="flex flex-wrap gap-1 border-b border-slate-200 print:hidden">
          {disponiveis.map((t) => (
            <button key={t.tipo} type="button" role="tab" aria-selected={t.tipo === actual?.tipo}
              onClick={() => setParametros((t.tipo === 'contas' || t.tipo === 'adiantamentos') && cliente ? { tipo: t.tipo, cliente: String(cliente.id) } : { tipo: t.tipo }, { replace: true })}
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
                className={`min-h-10 rounded-md px-3 text-sm font-medium ${v.vista === vista ? 'bg-superficie text-primaria-700 shadow' : 'text-slate-600'}`}>
                {v.titulo}
              </button>
            ))}
          </div>
          <div className="w-full max-w-md">
            <SeletorCliente valor={cliente} aoMudar={escolherCliente} rotulo={precisaCliente ? 'Cliente' : 'Cliente (opcional: vazio = todos)'} />
          </div>
        </div>
      )}

      {actual?.tipo === 'operacao' && (
        <div role="radiogroup" aria-label="Relatório de operação" className="flex flex-wrap gap-1 rounded-lg bg-slate-100 p-1 print:hidden">
          {VISTAS_OPERACAO.map((v) => (
            <button key={v.vista} type="button" role="radio" aria-checked={v.vista === vistaOperacao} onClick={() => mudarParametros({ vista: v.vista })}
              className={`min-h-10 rounded-md px-3 text-sm font-medium ${v.vista === vistaOperacao ? 'bg-superficie text-primaria-700 shadow' : 'text-slate-600'}`}>
              {v.titulo}
            </button>
          ))}
        </div>
      )}
      {actual?.tipo === 'adiantamentos' && (
        <div className="w-full max-w-md print:hidden">
          <SeletorCliente valor={cliente} aoMudar={escolherCliente} rotulo="Cliente (vazio = todos; escolhido = extrato do cliente)" />
        </div>
      )}

      <div className="flex flex-wrap items-end gap-2 print:hidden">
        {([['hoje', 'Hoje'], ['ontem', 'Ontem'], ['semana', 'Esta semana'], ['7dias', 'Últimos 7 dias'], ['mes', 'Este mês'], ['mesAnterior', 'Mês anterior'],
          ['ano', 'Este ano'], ['personalizado', 'Personalizado']] as const).map(([v, t]) => (
          <button key={v} type="button" onClick={() => setPeriodo(v)}
            className={`min-h-11 rounded-full px-4 text-sm font-medium ${periodo === v ? 'bg-primaria-600 text-white' : 'bg-superficie ring-1 ring-slate-300'}`}>{t}</button>
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
      {comArmazem && (
        <div className="print:hidden">
          <select className="campo w-64" value={armazemId ?? ''} aria-label="Armazém" onChange={(e) => mudarParametros({ armazem: e.target.value || null })}>
            <option value="">Todos os armazéns</option>
            {(armazens.dados ?? []).map((a) => <option key={a.id} value={a.id}>{a.nome}{a.activo ? '' : ' (inactivo)'}</option>)}
          </select>
        </div>
      )}
      <p className="text-sm text-slate-600">Período: {desde === ate ? desde : <>{desde}{' a '}{ate}</>} (hora de Maputo). {actual?.nota}</p>

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
      {actual?.tipo === 'adiantamentos' && (cliente ? <ExtratoAdiantamentosVista clienteId={cliente.id} desde={desde} ate={ate} /> : <RelatorioAdiantamentosVista desde={desde} ate={ate} />)}
      {actual?.tipo === 'contasPagar' && <ContasPagarVista desde={desde} ate={ate} />}
      {actual?.tipo === 'cotacoes' && <CotacoesVista desde={desde} ate={ate} />}
      {actual?.tipo === 'pontos' && <PontosVista desde={desde} ate={ate} />}
      {actual?.tipo === 'operacao' && vistaOperacao === 'horas' && <HorasVista desde={desde} ate={ate} />}
      {actual?.tipo === 'operacao' && vistaOperacao === 'cozinha' && <CozinhaVista desde={desde} ate={ate} />}
      {actual?.tipo === 'operacao' && vistaOperacao === 'mesas' && <MesasVista desde={desde} ate={ate} />}
      {actual?.tipo === 'operacao' && vistaOperacao === 'delivery' && <DeliveryVista desde={desde} ate={ate} />}
      {actual?.tipo === 'operacao' && vistaOperacao === 'reservas' && <ReservasVista desde={desde} ate={ate} />}
      {actual?.tipo === 'stock' && <RelatorioStockVista desde={desde} ate={ate} armazemId={armazemId} />}
      {actual?.tipo === 'acompanhamentos' && <RelatorioAcompanhamentosVista desde={desde} ate={ate} />}
      {actual?.tipo === 'existencias' && <RelatorioExistenciasVista armazemId={armazemId} />}
      {actual?.tipo === 'transferencias' && <RelatorioTransferenciasVista desde={desde} ate={ate} armazemId={armazemId} />}
    </div>
  )
}
