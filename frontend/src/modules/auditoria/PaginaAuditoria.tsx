import { Fragment, useState } from 'react'
import { ChevronDown, ChevronRight, Search } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Paginacao, Vazio } from '../../components/ui'
import { useAtraso } from '../../hooks/useAtraso'
import { useDados } from '../../hooks/useDados'
import type { OpcoesAuditoria, Pagina, RegistoAuditoria } from '../../types/api'
import { hojeIso, somarDias } from '../../utils/datas'
import { formatarDataHora } from '../../utils/formatar'
import { BotoesExportar } from '../relatorios/comum'
import { tabela } from '../relatorios/formato'
import { descreverAccao, nomeModulo } from './nomes'

const TAMANHO = 50

/** Detalhes guardados em JSON, apresentados linha a linha. */
function Detalhes({ texto }: { texto: string | null }) {
  if (!texto) return <p className="text-slate-500">Sem detalhes.</p>
  let bonito = texto
  try {
    bonito = JSON.stringify(JSON.parse(texto), null, 2)
  } catch {
    // texto simples
  }
  return <pre className="max-h-72 overflow-auto whitespace-pre-wrap break-all rounded bg-slate-50 p-3 text-xs">{bonito}</pre>
}

/**
 * Auditoria: quem fez o quê e quando (entradas no sistema, vendas, pagamentos, documentos, caixa, stock, configurações…).
 * Só consulta: os registos nunca se alteram nem se apagam.
 */
export function PaginaAuditoria() {
  const [periodo, setPeriodo] = useState({ desde: somarDias(hojeIso(), -6), ate: hojeIso() })
  const [modulo, setModulo] = useState('')
  const [utilizadorId, setUtilizadorId] = useState('')
  const [pesquisa, setPesquisa] = useState('')
  const [pagina, setPagina] = useState(1)
  const [aberto, setAberto] = useState<number | null>(null)
  const texto = useAtraso(pesquisa.trim())
  const opcoes = useDados(() => api.get<OpcoesAuditoria>('/api/auditoria/opcoes'), [])
  const filtro = { desde: periodo.desde, ate: periodo.ate, modulo: modulo || null, utilizadorId: utilizadorId || null, pesquisa: texto || null }
  const registos = useDados(() => api.get<Pagina<RegistoAuditoria>>('/api/auditoria' + query({ ...filtro, pagina, tamanho: TAMANHO })),
    [periodo.desde, periodo.ate, modulo, utilizadorId, texto, pagina])
  const mudar = (f: () => void) => { f(); setPagina(1); setAberto(null) }

  // Exportação: todos os registos do filtro (até 5000), página a página.
  const exportar = async () => {
    const linhas: RegistoAuditoria[] = []
    for (let p = 1; p <= 25; p++) {
      const r = await api.get<Pagina<RegistoAuditoria>>('/api/auditoria' + query({ ...filtro, pagina: p, tamanho: 200 }))
      linhas.push(...r.itens)
      if (linhas.length >= r.total) break
    }
    return tabela(`auditoria_${periodo.desde}_${periodo.ate}`, ['Data/hora', 'Utilizador', 'Módulo', 'Acção', 'Código', 'Registo', 'N.º', 'IP', 'Detalhes'],
      linhas.map((r) => [formatarDataHora(r.dataUtc), r.utilizador, nomeModulo(r.accao.split('.')[0]), descreverAccao(r.accao), r.accao, r.entidade, r.entidadeId, r.enderecoIp, r.detalhes]))
  }

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">Auditoria</h1>
        <BotoesExportar titulo="Auditoria" exportar={exportar} />
      </div>
      <p className="text-sm text-slate-600">Quem fez o quê e quando. Os registos são só de leitura: não se alteram nem se apagam.</p>

      <div className="cartao grid gap-3 p-4 md:grid-cols-[auto_auto_1fr_1fr_1.4fr]">
        <label className="text-sm">De<input type="date" className="campo" value={periodo.desde} max={periodo.ate} onChange={(e) => mudar(() => setPeriodo((p) => ({ ...p, desde: e.target.value })))} /></label>
        <label className="text-sm">Até<input type="date" className="campo" value={periodo.ate} min={periodo.desde} onChange={(e) => mudar(() => setPeriodo((p) => ({ ...p, ate: e.target.value })))} /></label>
        <label className="text-sm">Módulo
          <select className="campo" value={modulo} onChange={(e) => mudar(() => setModulo(e.target.value))}>
            <option value="">Todos</option>
            {opcoes.dados?.modulos.map((m) => <option key={m} value={m}>{nomeModulo(m)}</option>)}
          </select>
        </label>
        <label className="text-sm">Utilizador
          <select className="campo" value={utilizadorId} onChange={(e) => mudar(() => setUtilizadorId(e.target.value))}>
            <option value="">Todos</option>
            {opcoes.dados?.utilizadores.map((u) => <option key={u.id} value={u.id}>{u.nome}</option>)}
          </select>
        </label>
        <label className="relative text-sm">Pesquisar
          <Search className="pointer-events-none absolute bottom-3.5 left-3 h-4 w-4 text-slate-400" aria-hidden />
          <input type="search" className="campo pl-9" placeholder="N.º do pedido, factura, nome, IP…" value={pesquisa} onChange={(e) => mudar(() => setPesquisa(e.target.value))} />
        </label>
      </div>

      {registos.erro && <Alerta tipo="erro">{registos.erro}</Alerta>}
      {!registos.dados && !registos.erro && <Carregando />}
      {registos.dados?.total === 0 && <Vazio texto="Nenhum registo com estes filtros." />}
      {registos.dados && registos.dados.total > 0 && (
        <div className={`cartao overflow-x-auto ${registos.aCarregar ? 'opacity-60' : ''}`}>
          <p className="px-4 pt-3 text-sm text-slate-600">{registos.dados.total} registos</p>
          <table className="w-full text-left text-sm">
            <thead className="text-slate-600"><tr className="border-b border-slate-200">
              <th scope="col" className="w-8 px-2 py-2"><span className="sr-only">Detalhes</span></th>
              <th scope="col" className="px-2 py-2">Data/hora</th><th scope="col" className="px-2 py-2">Utilizador</th><th scope="col" className="px-2 py-2">Acção</th>
              <th scope="col" className="px-2 py-2">Registo</th><th scope="col" className="px-2 py-2">IP</th>
            </tr></thead>
            <tbody className="divide-y divide-slate-100">
              {registos.dados.itens.map((r) => (
                <Fragment key={r.id}>
                  <tr className="cursor-pointer hover:bg-slate-50" onClick={() => setAberto(aberto === r.id ? null : r.id)} aria-expanded={aberto === r.id}>
                    <td className="px-2 py-2 text-slate-400">{aberto === r.id ? <ChevronDown className="h-4 w-4" aria-hidden /> : <ChevronRight className="h-4 w-4" aria-hidden />}</td>
                    <td className="whitespace-nowrap px-2 py-2 tabular-nums">{formatarDataHora(r.dataUtc)}</td>
                    <td className="px-2 py-2">{r.utilizador ?? <span className="text-slate-400">sistema</span>}</td>
                    <td className="px-2 py-2">
                      <span className={r.accao.includes('falhado') || r.accao.includes('bloqueado') || r.accao.includes('anular') || r.accao.includes('estornar') ? 'font-medium text-red-700' : ''}>{descreverAccao(r.accao)}</span>
                      <span className="block text-xs text-slate-500">{nomeModulo(r.accao.split('.')[0])} · {r.accao}</span>
                    </td>
                    <td className="px-2 py-2 text-slate-600">{r.entidade ? `${r.entidade}${r.entidadeId ? ` n.º ${r.entidadeId}` : ''}` : '—'}</td>
                    <td className="px-2 py-2 text-slate-600">{r.enderecoIp ?? '—'}</td>
                  </tr>
                  {aberto === r.id && <tr><td /><td colSpan={5} className="px-2 pb-3"><Detalhes texto={r.detalhes} /></td></tr>}
                </Fragment>
              ))}
            </tbody>
          </table>
          {registos.dados.total > TAMANHO && <div className="px-4 pb-3"><Paginacao pagina={pagina} tamanho={TAMANHO} total={registos.dados.total} aoMudar={(p) => { setPagina(p); setAberto(null) }} /></div>}
        </div>
      )}
    </div>
  )
}
