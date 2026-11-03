import { useState } from 'react'
import { Search } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useAtraso } from '../../hooks/useAtraso'
import { useDados } from '../../hooks/useDados'
import type { RelatorioClientes } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { Indicadores, Seccao } from './comum'
import { percentagem, tabela } from './formato'

/** Vendas por cliente associado no POS («Associar cliente»); as restantes aparecem como «sem cliente». */
export function RelatorioClientesVista({ desde, ate }: { desde: string; ate: string }) {
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const rel = useDados(() => api.get<RelatorioClientes>('/api/relatorios/clientes' + query({ desde, ate, pesquisa: pesquisaAtrasada.trim() })),
    [desde, ate, pesquisaAtrasada])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const total = r.totalComCliente + r.totalSemCliente

  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Clientes com compras', String(r.clientes.length), `${r.vendasComCliente} vendas`],
        ['Vendas com cliente', formatarMoeda(r.totalComCliente), `${percentagem(r.totalComCliente, total)} do total`],
        ['Vendas sem cliente', formatarMoeda(r.totalSemCliente), `${r.vendasSemCliente} vendas`],
        ['Valor médio por cliente', formatarMoeda(r.clientes.length ? r.totalComCliente / r.clientes.length : 0), pesquisaAtrasada ? 'Com a pesquisa' : 'No período'],
      ]} />
      <Seccao titulo="Clientes"
        accoes={
          <div className="relative print:hidden">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
            <input type="search" className="campo min-h-9 pl-9" placeholder="Nome, telefone ou cartão…" value={pesquisa}
              onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar clientes" />
          </div>
        }
        exportar={() => tabela(`clientes_${desde}_${ate}`, ['Cliente', 'Telefone', 'Cartão', 'Compras', 'Total (MT)', 'Descontos (MT)', 'Última compra'],
          r.clientes.map((c) => [c.nome, c.telefone, c.cartao, c.compras, c.total, c.descontos, formatarDataHora(c.ultimaCompraUtc)]))}>
        {r.clientes.length === 0 ? (
          <Vazio texto={pesquisaAtrasada ? 'Nenhum cliente corresponde à pesquisa.' : 'Sem vendas com cliente associado no período (no POS: «Associar cliente»).'} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Cliente</th>
                  <th scope="col" className="py-2">Telefone</th>
                  <th scope="col" className="py-2">Cartão</th>
                  <th scope="col" className="py-2 text-right">Compras</th>
                  <th scope="col" className="py-2 text-right">Total</th>
                  <th scope="col" className="py-2 text-right">Descontos</th>
                  <th scope="col" className="py-2 text-right">Última compra</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.clientes.map((c) => (
                  <tr key={c.clienteId}>
                    <td className="py-1.5 font-medium">{c.nome}</td>
                    <td className="py-1.5 text-slate-600">{c.telefone ?? '—'}</td>
                    <td className="py-1.5 text-slate-600">{c.cartao ?? '—'}</td>
                    <td className="py-1.5 text-right">{c.compras}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(c.total)}</td>
                    <td className="py-1.5 text-right text-slate-600">{c.descontos ? formatarMoeda(c.descontos) : '—'}</td>
                    <td className="py-1.5 text-right text-slate-600">{formatarDataHora(c.ultimaCompraUtc)}</td>
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
