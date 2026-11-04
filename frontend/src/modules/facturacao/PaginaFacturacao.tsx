import { useState } from 'react'
import { FilePlus2, FileText, HandCoins, Loader2, Search } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Paginacao, Vazio } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { useDados } from '../../hooks/useDados'
import type { DocumentoResumo, EstadoDocumento, Pagina, TipoDocumento } from '../../types/api'
import { dataCurta, hojeIso } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { DetalheDocumento } from './DetalheDocumento'
import { FormDocumento } from './FormDocumento'
import { FormRecibo } from './FormRecibo'
import { NOMES_TIPO } from './nomes'

export function EtiquetaEstado({ tipo, estado, emDivida }: { tipo: TipoDocumento; estado: EstadoDocumento; emDivida: number }) {
  if (estado === 'Anulado') return <Etiqueta cor="vermelho">Anulado</Etiqueta>
  if (estado === 'Convertido') return <Etiqueta cor="cinza">Convertida</Etiqueta>
  if (tipo === 'Factura') return emDivida > 0 ? <Etiqueta cor="amarelo">Em dívida</Etiqueta> : <Etiqueta cor="verde">Liquidada</Etiqueta>
  return <Etiqueta cor="verde">Emitido</Etiqueta>
}

type Formulario = { tipo: 'Factura' | 'Cotacao' } | { tipo: 'Recibo'; clienteId?: number } | null

/**
 * Facturação: lista de todos os documentos (VD do POS, facturas, recibos, cotações e notas de crédito),
 * emissão de facturas, cotações e recibos, e o detalhe de cada documento (impressão, conversão, notas de crédito, anulação).
 */
export function PaginaFacturacao() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const [tipo, setTipo] = useState<TipoDocumento | ''>('')
  const [estado, setEstado] = useState<EstadoDocumento | ''>('')
  const [desde, setDesde] = useState(() => `${hojeIso().slice(0, 7)}-01`)
  const [ate, setAte] = useState(hojeIso)
  const [pesquisa, setPesquisa] = useState('')
  const [emDivida, setEmDivida] = useState(false)
  const [pagina, setPagina] = useState(1)
  const [aberto, setAberto] = useState<number | null>(null)
  const [formulario, setFormulario] = useState<Formulario>(null)
  const pesquisaAtrasada = useAtraso(pesquisa)
  const lista = useDados(
    () => api.get<Pagina<DocumentoResumo>>('/api/documentos' + query({
      tipo, estado, pesquisa: pesquisaAtrasada.trim(), emDivida: emDivida || undefined,
      // «Só em dívida» mostra todas as facturas por pagar, de qualquer data.
      desde: emDivida ? undefined : desde, ate: emDivida ? undefined : ate, pagina, tamanho: 50,
    })),
    [tipo, estado, desde, ate, pesquisaAtrasada, emDivida, pagina])
  const podeEmitir = tem('facturacao.emitir') && podeGravar
  const filtro = <T,>(definir: (v: T) => void) => (v: T) => { definir(v); setPagina(1) }

  const emitido = (id: number) => {
    setFormulario(null)
    setAberto(id)
    void lista.recarregar()
  }

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Facturação</h1>
        {tem('facturacao.emitir') && (
          <div className="flex flex-wrap gap-2">
            <button type="button" className="btn-secundario" disabled={!podeEmitir} onClick={() => setFormulario({ tipo: 'Cotacao' })}>
              <FileText className="h-4 w-4" aria-hidden /> Nova cotação
            </button>
            <button type="button" className="btn-secundario" disabled={!podeEmitir} onClick={() => setFormulario({ tipo: 'Recibo' })}>
              <HandCoins className="h-4 w-4" aria-hidden /> Novo recibo
            </button>
            <button type="button" className="btn-primario" disabled={!podeEmitir} onClick={() => setFormulario({ tipo: 'Factura' })}>
              <FilePlus2 className="h-4 w-4" aria-hidden /> Nova factura
            </button>
          </div>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <label className="relative">
          <span className="sr-only">Pesquisar</span>
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input className="campo w-72 pl-9" placeholder="N.º, cliente, NUIT ou pedido" value={pesquisa} onChange={(e) => filtro(setPesquisa)(e.target.value)} />
        </label>
        <select className="campo w-48" value={tipo} onChange={(e) => filtro(setTipo)(e.target.value as TipoDocumento | '')} aria-label="Tipo de documento">
          <option value="">Todos os documentos</option>
          {Object.entries(NOMES_TIPO).map(([v, n]) => <option key={v} value={v}>{n}</option>)}
        </select>
        <select className="campo w-40" value={estado} onChange={(e) => filtro(setEstado)(e.target.value as EstadoDocumento | '')} aria-label="Estado">
          <option value="">Todos os estados</option>
          <option value="Emitido">Emitidos</option>
          <option value="Anulado">Anulados</option>
          <option value="Convertido">Convertidos</option>
        </select>
        <label className="text-sm">De<input type="date" className="campo" value={desde} max={ate} disabled={emDivida} onChange={(e) => filtro(setDesde)(e.target.value)} /></label>
        <label className="text-sm">Até<input type="date" className="campo" value={ate} min={desde} disabled={emDivida} onChange={(e) => filtro(setAte)(e.target.value)} /></label>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" checked={emDivida} onChange={(e) => filtro(setEmDivida)(e.target.checked)} /> Só facturas em dívida
        </label>
      </div>

      <section className="cartao overflow-x-auto">
        {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
        {!lista.dados && !lista.erro && <Carregando />}
        {lista.dados?.itens.length === 0 && <Vazio texto="Sem documentos com estes filtros." />}
        {lista.dados && lista.dados.itens.length > 0 && (
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-600">
              <tr>
                <th scope="col" className="px-4 py-3">Documento</th>
                <th scope="col" className="px-4 py-3">Data</th>
                <th scope="col" className="px-4 py-3">Cliente</th>
                <th scope="col" className="px-4 py-3">Estado</th>
                <th scope="col" className="px-4 py-3 text-right">Total</th>
                <th scope="col" className="px-4 py-3 text-right">Em dívida</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {lista.dados.itens.map((d) => (
                <tr key={d.id} className={`cursor-pointer hover:bg-slate-50 ${d.estado === 'Anulado' ? 'opacity-60' : ''}`} onClick={() => setAberto(d.id)}>
                  <td className="px-4 py-2">
                    <button type="button" className="font-medium text-primaria-700 hover:underline" onClick={(e) => { e.stopPropagation(); setAberto(d.id) }}>{d.numero}</button>
                    <p className="text-xs text-slate-500">{NOMES_TIPO[d.tipo]}{d.origem && ` · ${d.origem}`}</p>
                  </td>
                  <td className="whitespace-nowrap px-4 py-2">
                    {formatarDataHora(d.emitidoEmUtc)}
                    {d.vencimento && <p className="text-xs text-slate-500">Vence {dataCurta(d.vencimento)}</p>}
                    {d.validaAte && <p className="text-xs text-slate-500">Válida até {dataCurta(d.validaAte)}</p>}
                  </td>
                  <td className="px-4 py-2">
                    <p>{d.clienteNome}</p>
                    {d.clienteNuit && <p className="text-xs text-slate-500">NUIT {d.clienteNuit}</p>}
                  </td>
                  <td className="px-4 py-2"><EtiquetaEstado tipo={d.tipo} estado={d.estado} emDivida={d.emDivida} /></td>
                  <td className="px-4 py-2 text-right font-medium tabular-nums">{d.tipo === 'NotaCredito' ? '−' : ''}{formatarMoeda(d.total)}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{d.emDivida > 0 ? formatarMoeda(d.emDivida) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {lista.dados && lista.dados.total > 0 && <Paginacao pagina={pagina} tamanho={50} total={lista.dados.total} aoMudar={setPagina} />}
        {lista.aCarregar && lista.dados && <div className="flex justify-center pb-2"><Loader2 className="h-4 w-4 animate-spin text-slate-400" aria-label="A actualizar" /></div>}
      </section>

      {aberto !== null && (
        <DetalheDocumento id={aberto} aoFechar={() => setAberto(null)} aoAbrir={setAberto} aoAlterar={() => void lista.recarregar()}
          aoReceber={(clienteId) => { setAberto(null); setFormulario({ tipo: 'Recibo', clienteId }) }} />
      )}
      {formulario?.tipo === 'Recibo' && <FormRecibo clienteInicial={formulario.clienteId} aoFechar={() => setFormulario(null)} aoEmitir={emitido} />}
      {(formulario?.tipo === 'Factura' || formulario?.tipo === 'Cotacao') && (
        <FormDocumento tipo={formulario.tipo} aoFechar={() => setFormulario(null)} aoEmitir={emitido} />
      )}
    </div>
  )
}
