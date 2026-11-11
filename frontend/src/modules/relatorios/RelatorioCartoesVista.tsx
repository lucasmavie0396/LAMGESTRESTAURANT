import { api, query } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { RelatorioCartoes, RelatorioDescargas, SessaoCartoes } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { NOME_MOVIMENTO_CARTAO } from '../cartoes/nomes'
import { Indicadores, Metodologia, Seccao } from './comum'
import { tabela } from './formato'

/**
 * Cartões Evento: dinheiro recebido, consumo, devoluções, saldos em circulação e todos os movimentos do período —
 * ou só de um caixa (sessão) do período.
 */
export function RelatorioCartoesVista({ desde, ate, sessao, aoMudarSessao }: {
  desde: string
  ate: string
  sessao: number | null
  aoMudarSessao: (sessao: number | null) => void
}) {
  const sessoes = useDados(() => api.get<SessaoCartoes[]>('/api/relatorios/cartoes/sessoes' + query({ desde, ate })), [desde, ate])
  const rel = useDados(() => api.get<RelatorioCartoes>('/api/relatorios/cartoes' + query({ desde, ate, sessaoCaixaId: sessao })), [desde, ate, sessao])
  const r = rel.dados
  const escolhaCaixa = (
    <label className="block max-w-xl text-sm print:hidden">Caixa
      <select className="campo" value={sessao ?? ''} onChange={(e) => aoMudarSessao(e.target.value ? Number(e.target.value) : null)}>
        <option value="">Todos os caixas (por datas)</option>
        {sessao !== null && !sessoes.dados?.some((x) => x.id === sessao) && <option value={sessao}>Caixa n.º {sessao}</option>}
        {sessoes.dados?.map((x) => (
          <option key={x.id} value={x.id}>
            N.º {x.id} · {x.terminal} · {x.abertaPor} · {formatarDataHora(x.abertaEmUtc)}{x.fechadaEmUtc ? '' : ' (aberto)'} · {x.movimentos} mov.
          </option>
        ))}
      </select>
    </label>
  )
  if (rel.erro) return <div className="space-y-3">{escolhaCaixa}<Alerta tipo="erro">{rel.erro}</Alerta></div>
  if (!r) return <Carregando />
  const s = r.resumo
  const sufixo = sessao !== null ? `caixa-${sessao}` : `${desde}_${ate}`

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      {escolhaCaixa}
      {sessao !== null && <p className="text-sm text-slate-600">Só os movimentos do caixa n.º {sessao} (o período acima serve só para escolher o caixa).</p>}
      <Indicadores itens={[
        ['Carregado e recarregado', formatarMoeda(s.carregado + s.recarregado), `${s.carregamentos} carregamentos · ${s.recargas} recargas`],
        ['Pago com cartões', formatarMoeda(s.pago), `${s.pagamentos} pagamentos`],
        ['Devolvido aos cartões', formatarMoeda(s.devolvido), `${s.devolucoes} anulações e estornos`],
        ['Saldo em circulação (hoje)', formatarMoeda(s.saldoEmCirculacao), `${s.cartoesComSaldo} cartões com saldo · ${s.cartoesNovos} novos no período`],
      ]} />

      <div className="grid gap-4 lg:grid-cols-2">
        <Seccao titulo="Dinheiro recebido por método" exportar={() => tabela(`cartoes-metodos_${sufixo}`, ['Método', 'Movimentos', 'Valor (MT)'],
          r.porMetodo.map((m) => [m.metodo, m.quantidade, m.valor]))}>
          {r.porMetodo.length === 0 ? <p className="text-sm text-slate-500">Nenhum no período.</p> : (
            <ul className="divide-y divide-slate-100 text-sm">
              {r.porMetodo.map((m) => <li key={m.metodo} className="flex justify-between py-1.5"><span>{m.metodo} ({m.quantidade})</span><span className="tabular-nums">{formatarMoeda(m.valor)}</span></li>)}
            </ul>
          )}
        </Seccao>
        <Seccao titulo="Por operador" exportar={() => tabela(`cartoes-operadores_${sufixo}`, ['Operador', 'Carregado (MT)', 'Pago com cartão (MT)', 'Movimentos'],
          r.porOperador.map((o) => [o.operador, o.carregado, o.pago, o.movimentos]))}>
          {r.porOperador.length === 0 ? <p className="text-sm text-slate-500">Nenhum no período.</p> : (
            <ul className="divide-y divide-slate-100 text-sm">
              {r.porOperador.map((o) => (
                <li key={o.operador} className="flex justify-between gap-3 py-1.5">
                  <span>{o.operador}</span>
                  <span className="text-right tabular-nums">carregou {formatarMoeda(o.carregado)} · recebeu {formatarMoeda(o.pago)}</span>
                </li>
              ))}
            </ul>
          )}
        </Seccao>
      </div>

      <SeccaoDescargas desde={desde} ate={ate} sessao={sessao} />

      <Seccao titulo="Saldos por cartão (hoje)" exportar={() => tabela('cartoes-saldos', ['Cartão', 'Titular', 'Saldo (MT)', 'Estado', 'Último movimento'],
        r.saldos.map((c) => [c.numero, c.titular, c.saldo, c.activo ? 'Activo' : 'Bloqueado', c.ultimoMovimentoUtc ? formatarDataHora(c.ultimoMovimentoUtc) : null]))}>
        {r.saldos.length === 0 ? <p className="text-sm text-slate-500">Nenhum cartão com saldo.</p> : (
          <div className="max-h-80 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Cartão</th><th scope="col" className="py-2">Titular</th><th scope="col" className="py-2 text-right">Saldo</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.saldos.map((c) => (
                  <tr key={c.numero}><td className="py-1.5">{c.numero}{!c.activo && ' (bloqueado)'}</td><td className="py-1.5">{c.titular ?? '—'}</td><td className="py-1.5 text-right">{formatarMoeda(c.saldo)}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>

      <Seccao titulo="Movimentos" exportar={() => tabela(`cartoes-movimentos_${sufixo}`,
        ['Data/hora', 'Cartão', 'Titular', 'Movimento', 'Valor (MT)', 'Saldo após (MT)', 'Método', 'Pedido', 'Operador', 'Motivo'],
        r.movimentos.map((m) => [formatarDataHora(m.dataUtc), m.numero, m.titular, NOME_MOVIMENTO_CARTAO[m.tipo], m.valor, m.saldoApos, m.metodo, m.pedido, m.operador, m.motivo]))}>
        {r.movimentosCortados && <Alerta tipo="aviso">Mostram-se só os primeiros 5000 movimentos. Escolha um período mais curto.</Alerta>}
        {r.movimentos.length === 0 ? <p className="text-sm text-slate-500">Nenhum no período.</p> : (
          <div className="max-h-96 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Data/hora</th><th scope="col" className="py-2">Cartão</th><th scope="col" className="py-2">Movimento</th>
                <th scope="col" className="py-2">Detalhe</th><th scope="col" className="py-2 text-right">Valor</th><th scope="col" className="py-2 text-right">Saldo</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.movimentos.map((m, i) => (
                  <tr key={i}>
                    <td className="py-1.5">{formatarDataHora(m.dataUtc)}</td>
                    <td className="py-1.5">{m.numero}</td>
                    <td className="py-1.5">{NOME_MOVIMENTO_CARTAO[m.tipo]}</td>
                    <td className="py-1.5 text-slate-600">{[m.metodo, m.pedido, m.operador].filter(Boolean).join(' · ')}</td>
                    <td className={`py-1.5 text-right ${m.valor < 0 ? 'text-red-700' : 'text-green-700'}`}>{formatarMoeda(m.valor)}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(m.saldoApos)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>

      <Metodologia>
        Pela data do movimento. O dinheiro dos carregamentos e recargas entra no caixa de quem os fez (já conta nos fechos); os pagamentos com cartão só
        abatem o saldo. Saldo em circulação: dinheiro recebido e ainda por consumir, à data de hoje.
      </Metodologia>
    </div>
  )
}

/** Descargas de saldo: cada cartão com o saldo descarregado, o acumulado e se o dinheiro foi devolvido. */
function SeccaoDescargas({ desde, ate, sessao }: { desde: string; ate: string; sessao: number | null }) {
  const rel = useDados(() => api.get<RelatorioDescargas>('/api/relatorios/cartoes/descargas' + query({ desde, ate, sessaoCaixaId: sessao })), [desde, ate, sessao])
  const r = rel.dados
  return (
    <Seccao titulo="Descargas de saldo" exportar={r ? () => tabela(sessao !== null ? `descargas-caixa-${sessao}` : `descargas_${desde}_${ate}`,
      ['Data', 'Cartão', 'Titular', 'Saldo (MT)', 'Acumulado (MT)', 'Destino', 'Método', 'Operador', 'Motivo', 'Caixa'],
      r.linhas.map((l) => [formatarDataHora(l.dataUtc), l.numero, l.titular, l.saldo, l.acumulado, l.devolveuDinheiro ? 'Devolvido' : 'Só retirado', l.metodo, l.operador, l.motivo, l.sessaoCaixaId])) : undefined}>
      {rel.erro && <Alerta tipo="erro">{rel.erro}</Alerta>}
      {r && r.linhas.length === 0 && <p className="text-sm text-slate-500">Sem descargas.</p>}
      {r && r.linhas.length > 0 && (
        <>
          <p className="mb-2 text-sm text-slate-600">
            {r.cartoes} cartões em {r.operacoes} descargas · total <span className="font-semibold">{formatarMoeda(r.total)}</span> · devolvido {formatarMoeda(r.devolvido)} · só retirado {formatarMoeda(r.retirado)}
          </p>
          <div className="max-h-80 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600"><tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Data</th><th scope="col" className="py-2">Cartão</th><th scope="col" className="py-2">Destino</th><th scope="col" className="py-2">Por</th>
                <th scope="col" className="py-2 text-right">Saldo</th><th scope="col" className="py-2 text-right">Acumulado</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.linhas.map((l, i) => (
                  <tr key={`${l.descargaId}-${i}`}>
                    <td className="py-1.5">{formatarDataHora(l.dataUtc)}</td>
                    <td className="py-1.5">{l.numero}{l.titular && <span className="text-slate-500"> · {l.titular}</span>}</td>
                    <td className="py-1.5">{l.devolveuDinheiro ? `Devolvido (${l.metodo})` : 'Só retirado'}</td>
                    <td className="py-1.5 text-slate-600">{l.operador ?? '—'}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(l.saldo)}</td>
                    <td className="py-1.5 text-right font-medium">{formatarMoeda(l.acumulado)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {r.cortado && <p className="mt-2 text-xs text-slate-500">Lista limitada aos primeiros 5000 cartões; os totais incluem todos.</p>}
        </>
      )}
    </Seccao>
  )
}
