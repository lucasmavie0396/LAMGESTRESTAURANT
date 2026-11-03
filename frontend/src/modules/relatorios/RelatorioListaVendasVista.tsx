import { useState } from 'react'
import { Filter, X } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Paginacao, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { EstadoPedido, ListaVendas, TipoPedido } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { Indicadores, Seccao } from './comum'
import { tabela } from './formato'

interface Filtros {
  estado: EstadoPedido
  tipo: '' | TipoPedido
  operadorId: string
  metodoPagamentoId: string
  horaDe: string
  horaAte: string
  pesquisa: string
}
const VAZIOS: Filtros = { estado: 'Pago', tipo: '', operadorId: '', metodoPagamentoId: '', horaDe: '', horaAte: '', pesquisa: '' }
const TAMANHO = 50
const NOME_TIPO: Record<TipoPedido, string> = { Mesa: 'Mesa', TakeAway: 'Take Away', Delivery: 'Delivery' }
const ESTADOS: { valor: EstadoPedido; rotulo: string }[] = [
  { valor: 'Pago', rotulo: 'Vendas (pagas)' },
  { valor: 'Estornado', rotulo: 'Estornadas' },
  { valor: 'Cancelado', rotulo: 'Canceladas' },
]

/** Vendas pedido a pedido. Os filtros só se aplicam ao carregar em «Aplicar»; os totais são de tudo o que foi filtrado. */
export function RelatorioListaVendasVista({ desde, ate }: { desde: string; ate: string }) {
  const [rascunho, setRascunho] = useState<Filtros>(VAZIOS)
  const [filtros, setFiltros] = useState<Filtros>(VAZIOS)
  const [pagina, setPagina] = useState(1)
  const parametros = (f: Filtros, extra: Record<string, string | number | boolean>) => query({
    desde, ate, estado: f.estado, tipo: f.tipo, operadorId: f.operadorId, metodoPagamentoId: f.metodoPagamentoId,
    horaDe: f.horaDe, horaAte: f.horaAte, pesquisa: f.pesquisa.trim(), ...extra,
  })
  const lista = useDados(() => api.get<ListaVendas>('/api/relatorios/lista-vendas' + parametros(filtros, { pagina, tamanho: TAMANHO })),
    [desde, ate, filtros, pagina])

  const aplicar = () => { setFiltros(rascunho); setPagina(1) }
  const limpar = () => { setRascunho(VAZIOS); setFiltros(VAZIOS); setPagina(1) }
  const alterar = (dados: Partial<Filtros>) => setRascunho((r) => ({ ...r, ...dados }))
  const vendas = filtros.estado === 'Pago'

  const exportar = async () => {
    const todas = await api.get<ListaVendas>('/api/relatorios/lista-vendas' + parametros(filtros, { todas: true }))
    return tabela(`lista-vendas_${desde}_${ate}`,
      ['Número', 'Data/hora', 'Tipo', 'Mesa', 'Cliente', 'Operador', 'Garçom', 'Subtotal', 'Desconto', 'Impostos', 'Taxa entrega', 'Total', 'Recebido', 'Troco', 'Métodos', 'Estado', 'Recibo', 'Motivo'],
      todas.vendas.itens.map((v) => [v.numero, formatarDataHora(v.dataUtc), NOME_TIPO[v.tipo], v.mesa, v.cliente, v.operador, v.garcom,
        v.subtotal, v.desconto, v.impostos, v.taxaEntrega, v.total, v.recebido, v.troco, v.metodos, v.estado, v.recibo, v.motivo]))
  }

  const r = lista.dados
  return (
    <div className="space-y-4">
      <section className="cartao p-4" aria-label="Filtros">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <label className="text-sm">Estado
            <select className="campo" value={rascunho.estado} onChange={(e) => alterar({ estado: e.target.value as EstadoPedido })}>
              {ESTADOS.map((s) => <option key={s.valor} value={s.valor}>{s.rotulo}</option>)}
            </select>
          </label>
          <label className="text-sm">Tipo de venda
            <select className="campo" value={rascunho.tipo} onChange={(e) => alterar({ tipo: e.target.value as Filtros['tipo'] })}>
              <option value="">Todos</option>
              {(Object.keys(NOME_TIPO) as TipoPedido[]).map((t) => <option key={t} value={t}>{NOME_TIPO[t]}</option>)}
            </select>
          </label>
          <label className="text-sm">Operador
            <select className="campo" value={rascunho.operadorId} onChange={(e) => alterar({ operadorId: e.target.value })}>
              <option value="">Todos</option>
              {r?.operadores.map((o) => <option key={o.id} value={o.id}>{o.nome}</option>)}
            </select>
          </label>
          <label className="text-sm">Forma de pagamento
            <select className="campo" value={rascunho.metodoPagamentoId} onChange={(e) => alterar({ metodoPagamentoId: e.target.value })}>
              <option value="">Todas</option>
              {r?.metodos.map((m) => <option key={m.id} value={m.id}>{m.nome}</option>)}
            </select>
          </label>
          <label className="text-sm">Hora de
            <input type="time" className="campo" value={rascunho.horaDe} onChange={(e) => alterar({ horaDe: e.target.value })} />
          </label>
          <label className="text-sm">Hora até
            <input type="time" className="campo" value={rascunho.horaAte} onChange={(e) => alterar({ horaAte: e.target.value })} />
          </label>
          <label className="text-sm sm:col-span-2">Pesquisa (n.º do pedido, cliente, contacto ou mesa)
            <input type="search" className="campo" value={rascunho.pesquisa} onChange={(e) => alterar({ pesquisa: e.target.value })}
              onKeyDown={(e) => { if (e.key === 'Enter') aplicar() }} />
          </label>
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" className="btn-primario" onClick={aplicar}><Filter className="h-4 w-4" aria-hidden /> Aplicar filtros</button>
          <button type="button" className="btn-secundario" onClick={limpar}><X className="h-4 w-4" aria-hidden /> Limpar filtros</button>
        </div>
        <p className="mt-2 text-xs text-slate-500">
          {vendas ? 'Vendas pela data/hora do pagamento.' : 'Pela data/hora do estorno ou cancelamento; não contam como facturação.'} As horas aplicam-se a cada dia do período.
        </p>
      </section>

      {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
      {!r && !lista.erro && <Carregando />}
      {r && (
        <div className={`space-y-4 ${lista.aCarregar ? 'opacity-60' : ''}`}>
          <Indicadores itens={[
            [vendas ? 'Total' : 'Total (não facturado)', formatarMoeda(r.totais.total), `${r.totais.vendas} ${vendas ? 'vendas' : 'pedidos'}`],
            ['Valor médio', formatarMoeda(r.totais.vendas ? r.totais.total / r.totais.vendas : 0), `Subtotal: ${formatarMoeda(r.totais.subtotal)}`],
            ['Descontos', formatarMoeda(r.totais.desconto), `Impostos incluídos: ${formatarMoeda(r.totais.impostos)}`],
            ['Recebido', formatarMoeda(r.totais.recebido), `Troco: ${formatarMoeda(r.totais.troco)}`],
          ]} />
          <Seccao titulo={vendas ? 'Vendas' : 'Pedidos'} exportar={exportar}>
            {r.vendas.itens.length === 0 ? <Vazio texto="Nenhuma venda com estes filtros." /> : (
              <div className="overflow-x-auto">
                <table className="w-full whitespace-nowrap text-left text-sm">
                  <thead className="text-slate-600">
                    <tr className="border-b border-slate-200">
                      <th scope="col" className="py-2 pr-3">N.º</th>
                      <th scope="col" className="py-2 pr-3">Data/hora</th>
                      <th scope="col" className="py-2 pr-3">Tipo</th>
                      <th scope="col" className="py-2 pr-3">Cliente</th>
                      <th scope="col" className="py-2 pr-3">Operador</th>
                      <th scope="col" className="py-2 pr-3 text-right">Subtotal</th>
                      <th scope="col" className="py-2 pr-3 text-right">Desconto</th>
                      <th scope="col" className="py-2 pr-3 text-right">IVA</th>
                      <th scope="col" className="py-2 pr-3 text-right">Total</th>
                      <th scope="col" className="py-2 pr-3 text-right">Recebido</th>
                      <th scope="col" className="py-2 pr-3 text-right">Troco</th>
                      <th scope="col" className="py-2 pr-3">Pagamento</th>
                      <th scope="col" className="py-2">{vendas ? 'Recibo' : 'Motivo'}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 tabular-nums">
                    {r.vendas.itens.map((v) => (
                      <tr key={v.id}>
                        <td className="py-1.5 pr-3 font-medium">{v.numero}</td>
                        <td className="py-1.5 pr-3">{formatarDataHora(v.dataUtc)}</td>
                        <td className="py-1.5 pr-3">{v.tipo === 'Mesa' ? `Mesa ${v.mesa ?? ''}` : NOME_TIPO[v.tipo]}</td>
                        <td className="py-1.5 pr-3 text-slate-600">{v.cliente ?? '—'}</td>
                        <td className="py-1.5 pr-3 text-slate-600">{v.operador ?? '—'}{v.garcom && <span className="text-xs"> · {v.garcom}</span>}</td>
                        <td className="py-1.5 pr-3 text-right">{formatarMoeda(v.subtotal)}</td>
                        <td className="py-1.5 pr-3 text-right text-slate-600">{v.desconto ? formatarMoeda(v.desconto) : '—'}</td>
                        <td className="py-1.5 pr-3 text-right text-slate-600">{formatarMoeda(v.impostos)}</td>
                        <td className="py-1.5 pr-3 text-right font-semibold">{formatarMoeda(v.total)}</td>
                        <td className="py-1.5 pr-3 text-right">{formatarMoeda(v.recebido)}</td>
                        <td className="py-1.5 pr-3 text-right text-slate-600">{v.troco ? formatarMoeda(v.troco) : '—'}</td>
                        <td className="py-1.5 pr-3 text-slate-600">{v.metodos || '—'}</td>
                        <td className="py-1.5">
                          {vendas ? (v.recibo ?? '—') : <span className="inline-flex items-center gap-1"><Etiqueta cor={v.estado === 'Estornado' ? 'vermelho' : 'cinza'}>{v.estado}</Etiqueta> {v.motivo}</span>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="border-t-2 border-slate-300 font-semibold tabular-nums">
                    <tr>
                      <td className="py-2 pr-3" colSpan={5}>Total ({r.totais.vendas})</td>
                      <td className="py-2 pr-3 text-right">{formatarMoeda(r.totais.subtotal)}</td>
                      <td className="py-2 pr-3 text-right">{formatarMoeda(r.totais.desconto)}</td>
                      <td className="py-2 pr-3 text-right">{formatarMoeda(r.totais.impostos)}</td>
                      <td className="py-2 pr-3 text-right">{formatarMoeda(r.totais.total)}</td>
                      <td className="py-2 pr-3 text-right">{formatarMoeda(r.totais.recebido)}</td>
                      <td className="py-2 pr-3 text-right">{formatarMoeda(r.totais.troco)}</td>
                      <td colSpan={2} />
                    </tr>
                  </tfoot>
                </table>
              </div>
            )}
            {r.vendas.total > TAMANHO && <div className="mt-2"><Paginacao pagina={pagina} tamanho={TAMANHO} total={r.vendas.total} aoMudar={setPagina} /></div>}
          </Seccao>
        </div>
      )}
    </div>
  )
}
