import { useEffect, useState } from 'react'
import { CheckCheck, ChefHat, Clock } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useEvento, useTempoReal } from '../../contexts/TempoRealContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { EstacaoPreparacao, EstadoItemCozinha, ItemCozinha, TicketCozinha } from '../../types/api'

const PROXIMO: Partial<Record<EstadoItemCozinha, { estado: EstadoItemCozinha; texto: string; classe: string }>> = {
  Pendente: { estado: 'EmPreparacao', texto: 'Iniciar', classe: 'bg-amber-500 hover:bg-amber-600' },
  EmPreparacao: { estado: 'Pronto', texto: 'Pronto', classe: 'bg-green-600 hover:bg-green-700' },
  Pronto: { estado: 'Entregue', texto: 'Entregue', classe: 'bg-slate-600 hover:bg-slate-700' },
}

const ROTULO: Record<EstadoItemCozinha, string> = {
  NaoEnviado: 'Por enviar',
  Pendente: 'Pendente',
  EmPreparacao: 'Em preparação',
  Pronto: 'Pronto',
  Entregue: 'Entregue',
}

/** Minutos de espera → alerta visual: até 10 normal, 10–20 amarelo, mais de 20 vermelho a piscar. */
function corEspera(minutos: number) {
  if (minutos >= 20) return 'border-red-500 bg-red-50 animate-pulse'
  if (minutos >= 10) return 'border-amber-400 bg-amber-50'
  return 'border-slate-300 bg-white'
}

export function PaginaCozinha() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const { estado: estadoTempoReal } = useTempoReal()
  const avisos = useAvisos()
  const podeActualizar = tem('cozinha.actualizar')
  const [estacaoId, setEstacaoId] = useState<number | null>(null)
  const [filtro, setFiltro] = useState<'activos' | 'Pendente' | 'EmPreparacao' | 'Pronto'>('activos')
  const [agora, setAgora] = useState(() => Date.now())
  const [aGravar, setAGravar] = useState<number | null>(null)

  const estacoes = useDados(() => api.get<EstacaoPreparacao[]>('/api/catalogo/estacoes'), [])
  const tickets = useDados(() => api.get<TicketCozinha[]>(`/api/cozinha${estacaoId ? `?estacaoId=${estacaoId}` : ''}`), [estacaoId])

  useEvento('pedidoAlterado', (d) => { if (d.afectaCozinha) void tickets.recarregar() })
  useEvento('religado', () => void tickets.recarregar())
  // Acerta o relógio a cada recarga, para tickets acabados de chegar não aparecerem com tempo negativo.
  useEffect(() => setAgora(Date.now()), [tickets.dados])

  // Relógio para os tempos de espera e verificação de segurança caso o tempo real falhe.
  useEffect(() => {
    const id = window.setInterval(() => {
      setAgora(Date.now())
      if (estadoTempoReal !== 'ligado') void tickets.recarregar()
    }, 15_000)
    return () => window.clearInterval(id)
  }, [estadoTempoReal, tickets.recarregar]) // eslint-disable-line react-hooks/exhaustive-deps

  const avancar = async (url: string, de: EstadoItemCozinha, para: EstadoItemCozinha, chave: number) => {
    setAGravar(chave)
    try {
      await api.post(url, { estadoActual: de, novoEstado: para })
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGravar(null)
      await tickets.recarregar()
    }
  }

  const visiveis = (tickets.dados ?? [])
    .map((t) => ({ ...t, itens: t.itens.filter((i) => filtro === 'activos' || i.estado === filtro) }))
    .filter((t) => t.itens.length > 0)
  const contar = (e: EstadoItemCozinha) => tickets.dados?.reduce((s, t) => s + t.itens.filter((i) => i.estado === e).length, 0) ?? 0

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex items-center gap-2 text-2xl font-semibold"><ChefHat className="h-7 w-7 text-primaria-600" aria-hidden /> Cozinha</h1>
        {estadoTempoReal !== 'ligado' && (
          <span className="rounded-full bg-amber-100 px-3 py-1 text-xs font-medium text-amber-900">Tempo real {estadoTempoReal === 'a-religar' ? 'a religar…' : 'desligado'} — a actualizar a cada 15 s</span>
        )}
        <div className="ml-auto flex flex-wrap gap-2">
          {([['activos', 'Todos'], ['Pendente', `Pendentes (${contar('Pendente')})`], ['EmPreparacao', `Em preparação (${contar('EmPreparacao')})`], ['Pronto', `Prontos (${contar('Pronto')})`]] as const).map(([v, t]) => (
            <button key={v} type="button" onClick={() => setFiltro(v)}
              className={`min-h-11 rounded-full px-4 text-sm font-medium ${filtro === v ? 'bg-primaria-600 text-white' : 'bg-white ring-1 ring-slate-300'}`}>{t}</button>
          ))}
          {(estacoes.dados?.length ?? 0) > 0 && (
            <select className="campo w-48" value={estacaoId ?? ''} onChange={(e) => setEstacaoId(e.target.value ? Number(e.target.value) : null)} aria-label="Estação">
              <option value="">Todas as estações</option>
              {estacoes.dados?.filter((e) => e.activo).map((e) => <option key={e.id} value={e.id}>{e.nome}</option>)}
            </select>
          )}
        </div>
      </div>

      {tickets.erro && <Alerta tipo="erro">{tickets.erro}</Alerta>}
      {!tickets.dados && !tickets.erro && <Carregando />}
      {tickets.dados && visiveis.length === 0 && <Vazio texto="Sem pedidos para preparar." />}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
        {visiveis.map((t) => {
          // Pequenas diferenças de relógio entre servidor e terminal nunca dão tempo negativo.
          const minutos = Math.max(0, Math.floor((agora - new Date(t.primeiroEnvioUtc).getTime()) / 60000))
          const pendentes = t.itens.filter((i) => i.estado === 'Pendente' || i.estado === 'EmPreparacao')
          return (
            <article key={t.pedidoId} className={`flex flex-col rounded-xl border-2 shadow-sm ${corEspera(pendentes.length > 0 ? minutos : 0)}`}>
              <header className="flex items-center justify-between border-b border-slate-200 px-4 py-2">
                <div>
                  <p className="text-xl font-bold">{t.tipo === 'Mesa' ? `Mesa ${t.mesa}` : t.tipo === 'TakeAway' ? 'Take Away' : t.tipo === 'Balcao' ? 'Balcão' : 'Delivery'}</p>
                  <p className="text-xs text-slate-500">{t.numero}</p>
                </div>
                <span className="flex items-center gap-1 text-lg font-semibold tabular-nums" title="Tempo desde o envio">
                  <Clock className="h-5 w-5" aria-hidden /> {minutos} min
                </span>
              </header>
              {t.observacoes && <p className="px-4 pt-2 text-sm italic text-amber-800">{t.observacoes}</p>}
              <ul className="flex-1 divide-y divide-slate-100">
                {t.itens.map((i: ItemCozinha) => {
                  const prox = PROXIMO[i.estado]
                  return (
                    <li key={i.id} className="flex items-center gap-3 px-4 py-2">
                      <span className="text-2xl font-bold tabular-nums">{i.quantidade}×</span>
                      <div className="min-w-0 flex-1">
                        <p className="text-lg font-medium leading-tight">{i.produto}</p>
                        {i.observacoes && <p className="text-sm font-semibold text-red-700">{i.observacoes}</p>}
                        <p className="text-xs text-slate-500">{ROTULO[i.estado]}{i.estacao && ` · ${i.estacao}`}</p>
                      </div>
                      {podeActualizar && prox && (
                        <button type="button" disabled={!podeGravar || aGravar !== null}
                          onClick={() => void avancar(`/api/cozinha/itens/${i.id}/estado`, i.estado, prox.estado, i.id)}
                          className={`min-h-12 min-w-24 rounded-lg px-3 font-semibold text-white disabled:opacity-50 ${prox.classe}`}>
                          {prox.texto}
                        </button>
                      )}
                    </li>
                  )
                })}
              </ul>
              {podeActualizar && t.itens.filter((i) => i.estado === 'EmPreparacao').length > 1 && (
                <button type="button" disabled={!podeGravar || aGravar !== null}
                  onClick={() => void avancar(`/api/cozinha/pedidos/${t.pedidoId}/estado`, 'EmPreparacao', 'Pronto', -t.pedidoId)}
                  className="m-3 inline-flex min-h-12 items-center justify-center gap-2 rounded-lg bg-green-600 font-semibold text-white hover:bg-green-700 disabled:opacity-50">
                  <CheckCheck className="h-5 w-5" aria-hidden /> Tudo pronto
                </button>
              )}
            </article>
          )
        })}
      </div>
    </div>
  )
}
