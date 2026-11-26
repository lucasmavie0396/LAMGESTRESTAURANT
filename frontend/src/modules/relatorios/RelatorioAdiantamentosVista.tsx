import { useState } from 'react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { ExtratoAdiantamentos, RelatorioAdiantamentos } from '../../types/api'
import { dataCurta, hojeIso } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { NOME_MOVIMENTO_CARTAO } from '../cartoes/nomes'
import { Indicadores, Metodologia, Seccao } from './comum'
import { tabela } from './formato'

/** Dias desde uma data (UTC) até hoje; null se nunca. */
function diasDesde(utc: string | null, hoje = hojeIso()) {
  if (!utc) return null
  return Math.floor((Date.parse(hoje) - Date.parse(utc.slice(0, 10))) / 86_400_000)
}

/**
 * Adiantamentos dos clientes (Cartões Evento associados a clientes): por cliente no período, com os saldos parados (sem
 * consumo há muitos dias), e recargas por método e por operador.
 */
export function RelatorioAdiantamentosVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useDados(() => api.get<RelatorioAdiantamentos>('/api/relatorios/adiantamentos' + query({ desde, ate })), [desde, ate])
  const [parados, setParados] = useState(0)
  const [ordem, setOrdem] = useState<'saldo' | 'consumos' | 'recargas'>('saldo')
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />

  const linhas = r.clientes
    .filter((c) => parados === 0 || (c.saldoFinal > 0 && (diasDesde(c.ultimoConsumoUtc) ?? Infinity) >= parados))
    .sort((a, b) => (ordem === 'consumos' ? b.consumos - a.consumos : ordem === 'recargas' ? b.recargas - a.recargas : b.saldoFinal - a.saldoFinal))
  const sufixo = `${desde}_${ate}`

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Recargas no período', formatarMoeda(r.recargas), `${r.recargasPorMetodo.reduce((s, m) => s + m.quantidade, 0)} recargas`],
        ['Consumido no período', formatarMoeda(r.consumos), 'Pagamentos com cartões de clientes'],
        [`Saldo em ${dataCurta(desde)}`, formatarMoeda(r.saldoInicial), 'Por consumir no início'],
        [`Devido aos clientes (${dataCurta(ate)})`, formatarMoeda(r.saldoFinal), `${r.clientes.filter((c) => c.saldoFinal > 0).length} clientes com saldo`],
      ]} />

      <Seccao titulo="Por cliente" exportar={() => tabela(`adiantamentos-clientes_${sufixo}`,
        ['Cliente', 'Telefone', 'Cartões', 'Saldo inicial (MT)', 'Recargas (MT)', 'Consumos (MT)', 'Outros (MT)', 'Saldo final (MT)', 'Última recarga', 'Último consumo'],
        linhas.map((c) => [c.cliente, c.telefone, c.cartoes, c.saldoInicial, c.recargas, c.consumos, c.outros, c.saldoFinal,
          c.ultimaRecargaUtc ? formatarDataHora(c.ultimaRecargaUtc) : null, c.ultimoConsumoUtc ? formatarDataHora(c.ultimoConsumoUtc) : null]))}>
        <div className="mb-2 flex flex-wrap items-end gap-3 print:hidden">
          <label className="text-sm">Ordenar por
            <select className="campo" value={ordem} onChange={(e) => setOrdem(e.target.value as typeof ordem)}>
              <option value="saldo">Saldo final</option><option value="consumos">Mais consumo</option><option value="recargas">Mais recargas</option>
            </select>
          </label>
          <label className="text-sm">Mostrar
            <select className="campo" value={parados} onChange={(e) => setParados(Number(e.target.value))}>
              <option value={0}>Todos</option>
              <option value={30}>Com saldo e sem consumo há 30 dias ou mais</option>
              <option value={60}>… há 60 dias ou mais</option>
              <option value={90}>… há 90 dias ou mais</option>
            </select>
          </label>
        </div>
        {linhas.length === 0 ? <p className="text-sm text-slate-500">{r.clientes.length === 0 ? 'Nenhum cliente com cartões e movimentos.' : 'Nenhum cliente neste filtro.'}</p> : (
          <div className="max-h-[32rem] overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-superficie text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Cliente</th><th scope="col" className="py-2 text-right">Inicial</th><th scope="col" className="py-2 text-right">Recargas</th>
                <th scope="col" className="py-2 text-right">Consumos</th><th scope="col" className="py-2 text-right">Outros</th><th scope="col" className="py-2 text-right">Final</th>
                <th scope="col" className="py-2">Último consumo</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {linhas.map((c) => {
                  const dias = diasDesde(c.ultimoConsumoUtc)
                  return (
                    <tr key={c.clienteId}>
                      <td className="py-1.5">{c.cliente}<span className="text-xs text-slate-500"> · {c.cartoes} cartão(ões)</span></td>
                      <td className="py-1.5 text-right">{formatarMoeda(c.saldoInicial)}</td>
                      <td className="py-1.5 text-right text-green-700">{formatarMoeda(c.recargas)}</td>
                      <td className="py-1.5 text-right text-red-700">{formatarMoeda(c.consumos)}</td>
                      <td className="py-1.5 text-right text-slate-600">{c.outros === 0 ? '' : formatarMoeda(c.outros)}</td>
                      <td className="py-1.5 text-right font-semibold">{formatarMoeda(c.saldoFinal)}</td>
                      <td className="py-1.5 text-slate-600">{dias === null ? 'nunca' : dias === 0 ? 'hoje' : `há ${dias} dias`}</td>
                    </tr>
                  )
                })}
              </tbody>
              <tfoot className="sticky bottom-0 bg-slate-50 font-semibold tabular-nums"><tr>
                <td className="py-2">Total ({linhas.length})</td>
                <td className="py-2 text-right">{formatarMoeda(linhas.reduce((s, c) => s + c.saldoInicial, 0))}</td>
                <td className="py-2 text-right">{formatarMoeda(linhas.reduce((s, c) => s + c.recargas, 0))}</td>
                <td className="py-2 text-right">{formatarMoeda(linhas.reduce((s, c) => s + c.consumos, 0))}</td>
                <td className="py-2 text-right">{formatarMoeda(linhas.reduce((s, c) => s + c.outros, 0))}</td>
                <td className="py-2 text-right">{formatarMoeda(linhas.reduce((s, c) => s + c.saldoFinal, 0))}</td>
                <td />
              </tr></tfoot>
            </table>
          </div>
        )}
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-2">
        <Seccao titulo="Recargas por método" exportar={() => tabela(`adiantamentos-metodos_${sufixo}`, ['Método', 'Recargas', 'Valor (MT)'],
          r.recargasPorMetodo.map((m) => [m.metodo, m.quantidade, m.valor]))}>
          {r.recargasPorMetodo.length === 0 ? <p className="text-sm text-slate-500">Nenhuma no período.</p> : (
            <ul className="divide-y divide-slate-100 text-sm">
              {r.recargasPorMetodo.map((m) => <li key={m.metodo} className="flex justify-between py-1.5"><span>{m.metodo} ({m.quantidade})</span><span className="tabular-nums">{formatarMoeda(m.valor)}</span></li>)}
            </ul>
          )}
        </Seccao>
        <Seccao titulo="Recargas por operador" exportar={() => tabela(`adiantamentos-operadores_${sufixo}`, ['Operador', 'Recargas', 'Valor (MT)'],
          r.recargasPorOperador.map((o) => [o.operador, o.quantidade, o.valor]))}>
          {r.recargasPorOperador.length === 0 ? <p className="text-sm text-slate-500">Nenhuma no período.</p> : (
            <ul className="divide-y divide-slate-100 text-sm">
              {r.recargasPorOperador.map((o) => <li key={o.operador} className="flex justify-between py-1.5"><span>{o.operador} ({o.quantidade})</span><span className="tabular-nums">{formatarMoeda(o.valor)}</span></li>)}
            </ul>
          )}
        </Seccao>
      </div>

      <Metodologia>
        Só os Cartões Evento associados a clientes. Cada movimento conta na conta do cliente a quem o cartão pertencia nesse momento. Consumos: pagamentos com o
        cartão menos anulações e estornos. Outros: descargas, transferências entre cartões e mudanças de cliente. A recarga não é venda: a VD sai no consumo.
      </Metodologia>
    </div>
  )
}

/** Extrato de adiantamentos de um cliente no período. */
export function ExtratoAdiantamentosVista({ clienteId, desde, ate }: { clienteId: number; desde: string; ate: string }) {
  const rel = useDados(() => api.get<ExtratoAdiantamentos>(`/api/cartoes/clientes/${clienteId}/extrato` + query({ desde, ate })), [clienteId, desde, ate])
  const e = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!e) return <Carregando />
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        [`Saldo anterior a ${dataCurta(desde)}`, formatarMoeda(e.saldoAnterior), e.clienteNome],
        ['Recargas', formatarMoeda(e.recargas), 'Entradas no período'],
        ['Consumos', formatarMoeda(e.consumos), e.outros !== 0 ? `Outros: ${formatarMoeda(e.outros)}` : 'Pagamentos com os cartões'],
        [`Saldo em ${dataCurta(ate)}`, formatarMoeda(e.saldoFinal), `Hoje: ${formatarMoeda(e.saldoActual)} em ${e.cartoes.length} cartão(ões)`],
      ]} />
      <Seccao titulo="Movimentos" exportar={() => tabela(`adiantamentos-${e.clienteNome}_${desde}_${ate}`,
        ['Data/hora', 'Cartão', 'Movimento', 'Valor (MT)', 'Saldo (MT)', 'Método', 'Pedido', 'Operador', 'Motivo'],
        e.movimentos.map((m) => [formatarDataHora(m.dataUtc), m.cartao, NOME_MOVIMENTO_CARTAO[m.tipo], m.valor, m.saldo, m.metodo, m.pedido, m.operador, m.motivo]))}>
        {e.cortado && <Alerta tipo="aviso">Mostram-se só os primeiros 5000 movimentos. Escolha um período mais curto.</Alerta>}
        {e.movimentos.length === 0 ? <p className="text-sm text-slate-500">Sem movimentos no período.</p> : (
          <div className="max-h-[32rem] overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-superficie text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Data/hora</th><th scope="col" className="py-2">Cartão</th><th scope="col" className="py-2">Movimento</th>
                <th scope="col" className="py-2">Detalhe</th><th scope="col" className="py-2 text-right">Valor</th><th scope="col" className="py-2 text-right">Saldo</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {e.movimentos.map((m) => (
                  <tr key={m.id}>
                    <td className="py-1.5">{formatarDataHora(m.dataUtc)}</td>
                    <td className="py-1.5">{m.cartao}</td>
                    <td className="py-1.5">{NOME_MOVIMENTO_CARTAO[m.tipo]}</td>
                    <td className="py-1.5 text-slate-600">{[m.metodo, m.pedido, m.operador, m.motivo].filter(Boolean).join(' · ')}</td>
                    <td className={`py-1.5 text-right ${m.valor < 0 ? 'text-red-700' : 'text-green-700'}`}>{m.valor > 0 ? '+' : ''}{formatarMoeda(m.valor)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(m.saldo)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>
    </div>
  )
}
