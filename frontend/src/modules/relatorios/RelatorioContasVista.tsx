import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { ExtratoContaCorrente, HistoricoCliente, RelatorioFacturasCredito, SituacaoFactura, TipoDocumento } from '../../types/api'
import { dataCurta } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { NOMES_TIPO } from '../facturacao/nomes'
import { Indicadores, Metodologia, Seccao } from './comum'
import { tabela } from './formato'

const SITUACAO: Record<SituacaoFactura, { texto: string; cor: 'verde' | 'cinza' | 'vermelho' | 'amarelo' | 'azul' }> = {
  Paga: { texto: 'Paga', cor: 'verde' },
  Creditada: { texto: 'Creditada', cor: 'cinza' },
  Parcial: { texto: 'Parcial', cor: 'azul' },
  EmDivida: { texto: 'Em dívida', cor: 'amarelo' },
  Vencida: { texto: 'Vencida', cor: 'vermelho' },
}

const data = (iso: string | null) => (iso ? dataCurta(iso) : '—')

/** Facturas a crédito emitidas no período (todas ou de um cliente) e a dívida de hoje por cliente, por antiguidade. */
export function FacturasCreditoVista({ desde, ate, clienteId }: { desde: string; ate: string; clienteId?: number }) {
  const rel = useDados(() => api.get<RelatorioFacturasCredito>('/api/documentos/facturas-credito' + query({ desde, ate, clienteId })), [desde, ate, clienteId])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const s = r.resumo
  const sufixo = `${desde}_${ate}`

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Facturado a crédito', formatarMoeda(s.facturado), `${s.facturas} facturas no período`],
        ['Pago (recibos)', formatarMoeda(s.pago), `creditado ${formatarMoeda(s.creditado)}`],
        ['Em dívida (destas facturas)', formatarMoeda(s.emDivida), s.vencido > 0 ? `vencido ${formatarMoeda(s.vencido)}` : 'nada vencido'],
        ['Dívida total de clientes (hoje)', formatarMoeda(r.totalEmDivida), r.totalVencido > 0 ? `vencido ${formatarMoeda(r.totalVencido)}` : 'nada vencido'],
      ]} />

      <Seccao titulo="Facturas do período" exportar={() => tabela(`facturas-credito_${sufixo}`,
        ['Data', 'Factura', 'Cliente', 'NUIT', 'Vencimento', 'Total (MT)', 'Pago (MT)', 'Creditado (MT)', 'Em dívida (MT)', 'Situação', 'Dias de atraso'],
        r.facturas.map((f) => [formatarDataHora(f.emitidoEmUtc), f.numero, f.clienteNome, f.clienteNuit, f.vencimento ? data(f.vencimento) : null,
          f.total, f.valorPago, f.valorCreditado, f.emDivida, SITUACAO[f.situacao].texto, f.diasAtraso]))}>
        {r.facturas.length === 0 ? <p className="text-sm text-slate-500">Sem facturas a crédito no período.</p> : (
          <div className="max-h-[28rem] overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Data</th><th scope="col" className="py-2">Factura</th><th scope="col" className="py-2">Cliente</th>
                <th scope="col" className="py-2">Vencimento</th><th scope="col" className="py-2 text-right">Total</th><th scope="col" className="py-2 text-right">Pago</th>
                <th scope="col" className="py-2 text-right">Em dívida</th><th scope="col" className="py-2">Situação</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.facturas.map((f) => (
                  <tr key={f.id}>
                    <td className="py-1.5">{formatarDataHora(f.emitidoEmUtc)}</td>
                    <td className="py-1.5 font-medium">{f.numero}</td>
                    <td className="py-1.5">{f.clienteNome}</td>
                    <td className="py-1.5">{data(f.vencimento)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(f.total)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(f.valorPago + f.valorCreditado)}</td>
                    <td className="py-1.5 text-right font-medium">{formatarMoeda(f.emDivida)}</td>
                    <td className="py-1.5"><Etiqueta cor={SITUACAO[f.situacao].cor}>{SITUACAO[f.situacao].texto}{f.diasAtraso > 0 ? ` há ${f.diasAtraso} d` : ''}</Etiqueta></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {r.cortado && <p className="mt-2 text-xs text-slate-500">Lista limitada às primeiras 5000 facturas; os totais incluem todas.</p>}
      </Seccao>

      <Seccao titulo={`Dívida de clientes hoje (${data(r.hoje)})`} exportar={() => tabela('dividas-clientes',
        ['Cliente', 'NUIT', 'Facturas em aberto', 'Em dívida (MT)', 'Não vencido (MT)', '1-30 dias (MT)', '31-60 dias (MT)', '61-90 dias (MT)', '+90 dias (MT)'],
        r.dividas.map((d) => [d.clienteNome, d.clienteNuit, d.facturas, d.emDivida, d.naoVencido, d.ate30, d.de31a60, d.de61a90, d.mais90]))}>
        {r.dividas.length === 0 ? <p className="text-sm text-slate-500">Nenhum cliente em dívida.</p> : (
          <div className="max-h-96 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Cliente</th><th scope="col" className="py-2 text-right">Facturas</th><th scope="col" className="py-2 text-right">Em dívida</th>
                <th scope="col" className="py-2 text-right">Não vencido</th><th scope="col" className="py-2 text-right">1-30 d</th><th scope="col" className="py-2 text-right">31-60 d</th>
                <th scope="col" className="py-2 text-right">61-90 d</th><th scope="col" className="py-2 text-right">+90 d</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.dividas.map((d) => (
                  <tr key={d.clienteId}>
                    <td className="py-1.5">{d.clienteNome}{d.clienteNuit && <span className="text-slate-500"> · {d.clienteNuit}</span>}</td>
                    <td className="py-1.5 text-right">{d.facturas}</td>
                    <td className="py-1.5 text-right font-medium">{formatarMoeda(d.emDivida)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(d.naoVencido)}</td>
                    {[d.ate30, d.de31a60, d.de61a90, d.mais90].map((v, i) => <td key={i} className={`py-1.5 text-right ${v > 0 ? 'text-red-700' : 'text-slate-400'}`}>{formatarMoeda(v)}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>
      <Metodologia>
        Facturas a crédito pela data de emissão. «Em dívida» = total − recibos em vigor − notas de crédito. A dívida de hoje inclui as facturas em aberto
        de qualquer data; a antiguidade conta os dias depois do vencimento.
      </Metodologia>
    </div>
  )
}

/** Todos os documentos do cliente no período: VD, facturas, recibos, notas de crédito e cotações. */
export function HistoricoClienteVista({ clienteId, desde, ate, tipo }: { clienteId: number; desde: string; ate: string; tipo?: TipoDocumento }) {
  const rel = useDados(() => api.get<HistoricoCliente>(`/api/documentos/historico-cliente/${clienteId}` + query({ desde, ate, tipo })), [clienteId, desde, ate, tipo])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const t = r.totais

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Facturas a crédito', formatarMoeda(t.facturado), `em dívida ${formatarMoeda(t.emDivida)}`],
        ['Vendas a dinheiro', formatarMoeda(t.vendasDinheiro), 'pagas no acto'],
        ['Recebido (recibos)', formatarMoeda(t.recebido), `notas de crédito ${formatarMoeda(t.creditado)}`],
        ['Documentos em vigor', String(t.documentos), `${r.documentos.length - t.documentos} anulados/convertidos`],
      ]} />
      <Seccao titulo={`Documentos de ${r.clienteNome}`} exportar={() => tabela(`historico_${r.clienteNome}_${desde}_${ate}`,
        ['Data', 'Tipo', 'Número', 'Estado', 'Vencimento', 'Total (MT)', 'Pago (MT)', 'Creditado (MT)', 'Em dívida (MT)', 'Origem', 'Forma de pagamento'],
        r.documentos.map((d) => [formatarDataHora(d.emitidoEmUtc), NOMES_TIPO[d.tipo], d.numero, d.estado, d.vencimento ? data(d.vencimento) : null,
          d.total, d.valorPago, d.valorCreditado, d.emDivida, d.origem, d.metodoPagamento]))}>
        {r.documentos.length === 0 ? <p className="text-sm text-slate-500">Sem documentos no período.</p> : (
          <div className="max-h-[32rem] overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Data</th><th scope="col" className="py-2">Documento</th><th scope="col" className="py-2">Referência</th>
                <th scope="col" className="py-2 text-right">Total</th><th scope="col" className="py-2 text-right">Em dívida</th><th scope="col" className="py-2">Estado</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.documentos.map((d) => (
                  <tr key={d.id} className={d.estado === 'Anulado' ? 'text-slate-400 line-through' : ''}>
                    <td className="py-1.5">{formatarDataHora(d.emitidoEmUtc)}</td>
                    <td className="py-1.5"><span className="font-medium">{d.numero}</span> <span className="text-slate-500">{NOMES_TIPO[d.tipo]}</span></td>
                    <td className="py-1.5 text-slate-600">{[d.origem, d.metodoPagamento, d.vencimento ? `venc. ${data(d.vencimento)}` : null].filter(Boolean).join(' · ')}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(d.total)}</td>
                    <td className="py-1.5 text-right">{d.emDivida > 0 ? formatarMoeda(d.emDivida) : '—'}</td>
                    <td className="py-1.5 no-underline"><Etiqueta cor={d.estado === 'Emitido' ? 'verde' : d.estado === 'Anulado' ? 'vermelho' : 'cinza'}>{d.estado}</Etiqueta></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {r.cortado && <p className="mt-2 text-xs text-slate-500">Lista limitada aos primeiros 5000 documentos.</p>}
      </Seccao>
    </div>
  )
}

/** Extrato da conta corrente: saldo anterior, movimentos com saldo acumulado, saldo final e facturas em aberto hoje. */
export function ExtratoVista({ clienteId, desde, ate }: { clienteId: number; desde: string; ate: string }) {
  const rel = useDados(() => api.get<ExtratoContaCorrente>(`/api/documentos/conta-corrente/${clienteId}/extrato` + query({ desde, ate })), [clienteId, desde, ate])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        [`Saldo anterior a ${data(r.desde)}`, formatarMoeda(r.saldoAnterior), 'facturas por pagar antes do período'],
        ['Débitos (facturas)', formatarMoeda(r.debitos), `${r.movimentos.filter((m) => m.debito > 0).length} facturas`],
        ['Créditos', formatarMoeda(r.creditos), 'recibos e notas de crédito'],
        [`Saldo em ${data(r.ate)}`, formatarMoeda(r.saldoFinal), `hoje em dívida ${formatarMoeda(r.emDividaHoje)}${r.vencidoHoje > 0 ? ` · vencido ${formatarMoeda(r.vencidoHoje)}` : ''}`],
      ]} />
      <Seccao titulo={`Extrato de ${r.clienteNome}`} exportar={() => tabela(`extrato_${r.clienteNome}_${desde}_${ate}`,
        ['Data', 'Documento', 'Descrição', 'Vencimento', 'Débito (MT)', 'Crédito (MT)', 'Saldo (MT)'],
        [[null, null, `Saldo anterior a ${data(r.desde)}`, null, null, null, r.saldoAnterior],
          ...r.movimentos.map((m) => [formatarDataHora(m.dataUtc), m.numero, m.descricao, m.vencimento ? data(m.vencimento) : null,
            m.debito || null, m.credito || null, m.saldo]),
          [null, null, `Saldo em ${data(r.ate)}`, null, r.debitos, r.creditos, r.saldoFinal]])}>
        <div className="max-h-[32rem] overflow-auto">
          <table className="w-full text-left text-sm">
            <thead className="sticky top-0 bg-white text-slate-600"><tr className="border-b border-slate-200">
              <th scope="col" className="py-2">Data</th><th scope="col" className="py-2">Documento</th><th scope="col" className="py-2">Descrição</th>
              <th scope="col" className="py-2 text-right">Débito</th><th scope="col" className="py-2 text-right">Crédito</th><th scope="col" className="py-2 text-right">Saldo</th>
            </tr></thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              <tr className="bg-slate-50 font-medium"><td className="py-1.5" colSpan={5}>Saldo anterior a {data(r.desde)}</td><td className="py-1.5 text-right">{formatarMoeda(r.saldoAnterior)}</td></tr>
              {r.movimentos.length === 0 && <tr><td colSpan={6} className="py-2 text-slate-500">Sem movimentos no período.</td></tr>}
              {r.movimentos.map((m) => (
                <tr key={`${m.tipo}-${m.documentoId}`}>
                  <td className="py-1.5">{formatarDataHora(m.dataUtc)}</td>
                  <td className="py-1.5 font-medium">{m.numero}</td>
                  <td className="py-1.5">{m.descricao}{m.vencimento && <span className="text-slate-500"> · venc. {data(m.vencimento)}</span>}</td>
                  <td className="py-1.5 text-right">{m.debito > 0 ? formatarMoeda(m.debito) : ''}</td>
                  <td className="py-1.5 text-right text-green-700">{m.credito > 0 ? formatarMoeda(m.credito) : ''}</td>
                  <td className="py-1.5 text-right font-medium">{formatarMoeda(m.saldo)}</td>
                </tr>
              ))}
              <tr className="border-t-2 border-slate-300 font-semibold">
                <td className="py-2" colSpan={3}>Saldo em {data(r.ate)}</td>
                <td className="py-2 text-right">{formatarMoeda(r.debitos)}</td><td className="py-2 text-right">{formatarMoeda(r.creditos)}</td>
                <td className="py-2 text-right">{formatarMoeda(r.saldoFinal)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </Seccao>
      <Seccao titulo={`Facturas em aberto hoje (${r.facturasEmAberto.length})`}>
        {r.facturasEmAberto.length === 0 ? <p className="text-sm text-slate-500">Nenhuma factura por pagar.</p> : (
          <ul className="divide-y divide-slate-100 text-sm">
            {r.facturasEmAberto.map((f) => (
              <li key={f.id} className="flex flex-wrap justify-between gap-2 py-1.5">
                <span><span className="font-medium">{f.numero}</span> · {formatarDataHora(f.emitidoEmUtc)}{f.vencimento && ` · venc. ${data(f.vencimento)}`}
                  {f.vencida && <> <Etiqueta cor="vermelho">Vencida</Etiqueta></>}</span>
                <span className="tabular-nums">{formatarMoeda(f.emDivida)} <span className="text-slate-500">de {formatarMoeda(f.total)}</span></span>
              </li>
            ))}
          </ul>
        )}
      </Seccao>
      <Metodologia>
        A conta corrente só tem facturas a crédito (débito), recibos em vigor e notas de crédito sobre facturas (crédito). As vendas a dinheiro são pagas
        no acto e não entram; os recibos anulados também não.
      </Metodologia>
    </div>
  )
}
