import { api } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { PedidoAnulado, RelatorioAnulacoes } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { Indicadores, Metodologia, Seccao } from './comum'
import { n3, tabela } from './formato'

const soma = (valores: { valor: number }[]) => valores.reduce((s, v) => s + v.valor, 0)

function TabelaPedidos({ linhas, comRecibo }: { linhas: PedidoAnulado[]; comRecibo?: boolean }) {
  if (linhas.length === 0) return <p className="text-sm text-slate-500">Nenhum no período.</p>
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="text-slate-600">
          <tr className="border-b border-slate-200">
            <th scope="col" className="py-2 pr-3">Pedido</th>
            <th scope="col" className="py-2 pr-3">Data/hora</th>
            {comRecibo && <th scope="col" className="py-2 pr-3">Recibo</th>}
            <th scope="col" className="py-2 pr-3">Por</th>
            <th scope="col" className="py-2 pr-3">Motivo</th>
            <th scope="col" className="py-2 text-right">Valor</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 tabular-nums">
          {linhas.map((p) => (
            <tr key={p.id}>
              <td className="py-1.5 pr-3 font-medium">{p.numero}{p.mesa && <span className="text-xs text-slate-500"> · Mesa {p.mesa}</span>}</td>
              <td className="py-1.5 pr-3">{formatarDataHora(p.dataUtc)}</td>
              {comRecibo && <td className="py-1.5 pr-3 text-slate-600">{p.recibo ?? '—'}</td>}
              <td className="py-1.5 pr-3 text-slate-600">{p.por ?? '—'}</td>
              <td className="py-1.5 pr-3">{p.motivo ?? '—'}</td>
              <td className="py-1.5 text-right">{formatarMoeda(p.valor)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** Anulações e devoluções do período, com quem e porquê. Nenhum destes valores conta como facturação. */
export function RelatorioAnulacoesVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useDados(() => api.get<RelatorioAnulacoes>(`/api/relatorios/anulacoes?desde=${desde}&ate=${ate}`), [desde, ate])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const sufixo = `${desde}_${ate}`
  const colunasPedido = ['Pedido', 'Data/hora', 'Mesa', 'Por', 'Motivo', 'Valor (MT)', 'Recibo']
  const linhasPedido = (l: PedidoAnulado[]) => l.map((p) => [p.numero, formatarDataHora(p.dataUtc), p.mesa, p.por, p.motivo, p.valor, p.recibo])

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Estornos (devoluções)', formatarMoeda(soma(r.estornos)), `${r.estornos.length} pedidos`],
        ['Pedidos cancelados', formatarMoeda(soma(r.pedidosCancelados)), `${r.pedidosCancelados.length} pedidos`],
        ['Itens cancelados após envio', formatarMoeda(soma(r.itensCancelados)), `${r.itensCancelados.length} itens`],
        ['Pagamentos anulados', formatarMoeda(soma(r.pagamentosAnulados)), `${r.pagamentosAnulados.length} pagamentos`],
      ]} />

      <Seccao titulo="Estornos (pedidos pagos devolvidos)" exportar={() => tabela(`estornos_${sufixo}`, colunasPedido, linhasPedido(r.estornos))}>
        <TabelaPedidos linhas={r.estornos} comRecibo />
      </Seccao>
      <Seccao titulo="Pedidos cancelados" exportar={() => tabela(`pedidos-cancelados_${sufixo}`, colunasPedido, linhasPedido(r.pedidosCancelados))}>
        <TabelaPedidos linhas={r.pedidosCancelados} />
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-2">
        <Seccao titulo="Itens cancelados depois de enviados"
          exportar={() => tabela(`itens-cancelados_${sufixo}`, ['Pedido', 'Data/hora', 'Produto', 'Quantidade', 'Valor (MT)', 'Por', 'Motivo'],
            r.itensCancelados.map((i) => [i.pedido, formatarDataHora(i.dataUtc), i.produto, i.quantidade, i.valor, i.por, i.motivo]))}>
          {r.itensCancelados.length === 0 ? <p className="text-sm text-slate-500">Nenhum no período.</p> : (
            <ul className="divide-y divide-slate-100 text-sm">
              {r.itensCancelados.map((i, n) => (
                <li key={`${i.pedidoId}-${n}`} className="flex justify-between gap-3 py-1.5">
                  <span><strong>{n3.format(i.quantidade)} x {i.produto}</strong> · {i.pedido}<br />
                    <span className="text-xs text-slate-600">{formatarDataHora(i.dataUtc)} · {i.por ?? '—'}: {i.motivo ?? '—'}</span></span>
                  <span className="tabular-nums">{formatarMoeda(i.valor)}</span>
                </li>
              ))}
            </ul>
          )}
        </Seccao>
        <Seccao titulo="Pagamentos anulados"
          exportar={() => tabela(`pagamentos-anulados_${sufixo}`, ['Pedido', 'Data/hora', 'Método', 'Valor (MT)', 'Por', 'Motivo'],
            r.pagamentosAnulados.map((p) => [p.pedido, formatarDataHora(p.dataUtc), p.metodo, p.valor, p.por, p.motivo]))}>
          {r.pagamentosAnulados.length === 0 ? <p className="text-sm text-slate-500">Nenhum no período.</p> : (
            <ul className="divide-y divide-slate-100 text-sm">
              {r.pagamentosAnulados.map((p, n) => (
                <li key={`${p.pedidoId}-${n}`} className="flex justify-between gap-3 py-1.5">
                  <span><strong>{p.metodo}</strong> · {p.pedido}<br />
                    <span className="text-xs text-slate-600">{formatarDataHora(p.dataUtc)} · {p.por ?? '—'}: {p.motivo ?? '—'}</span></span>
                  <span className="tabular-nums">{formatarMoeda(p.valor)}</span>
                </li>
              ))}
            </ul>
          )}
        </Seccao>
      </div>

      <Metodologia>
        Pela data em que aconteceram. Estorno = pedido pago devolvido na totalidade (o valor saiu do caixa como reembolso). Cancelados: valor dos itens do
        pedido no momento do cancelamento, antes do desconto (sem os que foram retirados antes do envio). Nenhum destes valores conta como venda; os
        pagamentos anulados eram parciais de pedidos ainda abertos.
      </Metodologia>
    </div>
  )
}
