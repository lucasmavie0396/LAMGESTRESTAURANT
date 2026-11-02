import { Etiqueta } from '../../components/ui'
import type { SessaoCaixa } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'

function Indicador({ rotulo, valor, destaque }: { rotulo: string; valor: string; destaque?: 'verde' | 'vermelho' }) {
  const cor = destaque === 'verde' ? 'text-green-700' : destaque === 'vermelho' ? 'text-red-700' : 'text-slate-900'
  return (
    <div className="cartao p-4">
      <p className="text-sm text-slate-500">{rotulo}</p>
      <p className={`mt-1 text-xl font-semibold tabular-nums ${cor}`}>{valor}</p>
    </div>
  )
}

const corDiferenca = (d: number | null) => (d === null || d === 0 ? undefined : d > 0 ? 'verde' : 'vermelho')

/** Resumo de uma sessão de caixa (aberta ou fechada). Também serve de relatório de abertura/fecho imprimível. */
export function ResumoSessao({ sessao }: { sessao: SessaoCaixa }) {
  const fechada = sessao.estado === 'Fechada'
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-1 text-sm text-slate-600">
        <span>
          <strong className="text-slate-800">Terminal:</strong> {sessao.terminal}
        </span>
        <span>
          <strong className="text-slate-800">Aberta por:</strong> {sessao.abertaPor} em {formatarDataHora(sessao.abertaEmUtc)}
        </span>
        {fechada && sessao.fechadaEmUtc && (
          <span>
            <strong className="text-slate-800">Fechada por:</strong> {sessao.fechadaPor} em {formatarDataHora(sessao.fechadaEmUtc)}
          </span>
        )}
        {fechada ? <Etiqueta cor="cinza">Fechada</Etiqueta> : <Etiqueta cor="verde">Aberta</Etiqueta>}
      </div>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Indicador rotulo="Fundo inicial" valor={formatarMoeda(sessao.fundoInicial)} />
        <Indicador rotulo={`Recebido (${sessao.pedidosPagos} pedidos)`} valor={formatarMoeda(sessao.totalRecebido)} />
        <Indicador rotulo="Total esperado" valor={formatarMoeda(sessao.totalEsperado)} />
        {fechada ? (
          <Indicador rotulo="Diferença no fecho" valor={formatarMoeda(sessao.totalDiferenca ?? 0)} destaque={corDiferenca(sessao.totalDiferenca)} />
        ) : (
          <Indicador rotulo="Movimentos manuais" valor={String(sessao.movimentos.length)} />
        )}
      </div>

      <div className="cartao overflow-x-auto">
        <table className="w-full text-left text-sm">
          <caption className="px-4 pt-3 text-left font-semibold">Por método de pagamento</caption>
          <thead className="text-slate-600">
            <tr className="border-b border-slate-200">
              <th scope="col" className="px-4 py-2">Método</th>
              <th scope="col" className="px-4 py-2 text-right">Fundo</th>
              <th scope="col" className="px-4 py-2 text-right">Recebido</th>
              <th scope="col" className="px-4 py-2 text-right">Entradas</th>
              <th scope="col" className="px-4 py-2 text-right">Saídas</th>
              <th scope="col" className="px-4 py-2 text-right">Esperado</th>
              {fechada && <th scope="col" className="px-4 py-2 text-right">Contado</th>}
              {fechada && <th scope="col" className="px-4 py-2 text-right">Diferença</th>}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 tabular-nums">
            {sessao.porMetodo.map((m) => (
              <tr key={m.metodoPagamentoId}>
                <td className="px-4 py-2 font-medium">{m.metodo}</td>
                <td className="px-4 py-2 text-right">{formatarMoeda(m.fundo)}</td>
                <td className="px-4 py-2 text-right">{formatarMoeda(m.recebimentos)}</td>
                <td className="px-4 py-2 text-right">{formatarMoeda(m.entradas)}</td>
                <td className="px-4 py-2 text-right">{formatarMoeda(m.saidas)}</td>
                <td className="px-4 py-2 text-right font-semibold">{formatarMoeda(m.esperado)}</td>
                {fechada && <td className="px-4 py-2 text-right">{formatarMoeda(m.contado ?? 0)}</td>}
                {fechada && (
                  <td className={`px-4 py-2 text-right font-semibold ${m.diferenca ? (m.diferenca > 0 ? 'text-green-700' : 'text-red-700') : ''}`}>
                    {formatarMoeda(m.diferenca ?? 0)}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="cartao">
        <h3 className="px-4 pt-3 font-semibold">Entradas e saídas manuais</h3>
        {sessao.movimentos.length === 0 ? (
          <p className="px-4 py-3 text-sm text-slate-500">Sem movimentos registados.</p>
        ) : (
          <ul className="divide-y divide-slate-100">
            {sessao.movimentos.map((m) => (
              <li key={m.id} className="flex items-center gap-3 px-4 py-2 text-sm">
                <Etiqueta cor={m.tipo === 'Entrada' ? 'verde' : 'vermelho'}>{m.tipo === 'Entrada' ? 'Entrada' : m.tipo === 'Saida' ? 'Saída' : 'Despesa'}</Etiqueta>
                <span className="flex-1">
                  {m.motivo} <span className="text-slate-500">· {m.metodo} · {m.utilizador} · {formatarDataHora(m.dataUtc)}</span>
                </span>
                <span className="font-semibold tabular-nums">{formatarMoeda(m.valor)}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      {(sessao.observacoesAbertura || sessao.observacoesFecho) && (
        <div className="text-sm text-slate-600">
          {sessao.observacoesAbertura && <p><strong>Observações na abertura:</strong> {sessao.observacoesAbertura}</p>}
          {sessao.observacoesFecho && <p><strong>Observações no fecho:</strong> {sessao.observacoesFecho}</p>}
        </div>
      )}
    </div>
  )
}
