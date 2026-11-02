import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { MapPin, Phone, ShoppingBag, Truck } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useEvento } from '../../contexts/TempoRealContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { EstadoEntrega, Pagina, Pedido, PedidoResumo } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'

const COLUNAS: { estado: EstadoEntrega; titulo: string }[] = [
  { estado: 'Recebido', titulo: 'Recebidos' },
  { estado: 'EmPreparacao', titulo: 'Em preparação' },
  { estado: 'Pronto', titulo: 'Prontos' },
  { estado: 'SaiuParaEntrega', titulo: 'Saíram para entrega' },
]

/** Próximo passo logístico (as transições são validadas no servidor). */
function proximos(p: PedidoResumo): { estado: EstadoEntrega; texto: string }[] {
  switch (p.estadoEntrega) {
    case 'Recebido':
    case 'EmPreparacao':
      return [{ estado: 'Pronto', texto: 'Marcar pronto' }]
    case 'Pronto':
      return p.tipo === 'Delivery' ? [{ estado: 'SaiuParaEntrega', texto: 'Saiu para entrega' }] : [{ estado: 'Entregue', texto: 'Entregue ao cliente' }]
    case 'SaiuParaEntrega':
      return [{ estado: 'Entregue', texto: 'Entregue' }]
    default:
      return []
  }
}

export function PaginaDelivery() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const navegar = useNavigate()
  const [aGravar, setAGravar] = useState<number | null>(null)
  const lista = useDados(() => api.get<Pagina<PedidoResumo>>('/api/pedidos?entregaActiva=true&tamanho=200'), [])

  useEvento('pedidoAlterado', () => void lista.recarregar())
  useEvento('religado', () => void lista.recarregar())

  const avancar = async (resumo: PedidoResumo, estado: EstadoEntrega) => {
    setAGravar(resumo.id)
    try {
      // A RowVersion actual vem do pedido completo (o resumo da lista não a inclui).
      const pedido = await api.get<Pedido>(`/api/pedidos/${resumo.id}`)
      await api.post(`/api/pedidos/${resumo.id}/entrega`, { estado, estafetaId: null, rowVersion: pedido.rowVersion })
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGravar(null)
    }
  }

  const pedidos = lista.dados?.itens ?? []
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">Delivery e Take Away</h1>
        {tem('pos.usar') && (
          <button type="button" className="btn-primario" onClick={() => navegar('/pos')}>Novo pedido no POS</button>
        )}
      </div>
      {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados && pedidos.length === 0 && <Vazio texto="Sem pedidos de Delivery ou Take Away activos." />}

      {pedidos.length > 0 && (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {COLUNAS.map((c) => {
            const daColuna = pedidos.filter((p) => p.estadoEntrega === c.estado)
            return (
              <section key={c.estado} className="rounded-xl bg-slate-200/60 p-3" aria-label={c.titulo}>
                <h2 className="mb-2 font-semibold text-slate-700">{c.titulo} ({daColuna.length})</h2>
                <div className="space-y-2">
                  {daColuna.map((p) => (
                    <article key={p.id} className="cartao p-3">
                      <div className="flex items-start justify-between gap-2">
                        <span className="flex items-center gap-1.5 font-semibold">
                          {p.tipo === 'Delivery' ? <Truck className="h-4 w-4" aria-hidden /> : <ShoppingBag className="h-4 w-4" aria-hidden />}
                          {p.tipo === 'Delivery' ? 'Delivery' : 'Take Away'}
                        </span>
                        {p.estado === 'Pago' ? <Etiqueta cor="verde">Pago</Etiqueta> : <Etiqueta cor="amarelo">Por pagar {formatarMoeda(p.total - p.totalPago)}</Etiqueta>}
                      </div>
                      <p className="text-xs text-slate-500">{p.numero} · {formatarDataHora(p.criadoEmUtc)}</p>
                      {p.contactoEntrega && <p className="mt-1 flex items-center gap-1 text-sm"><Phone className="h-3.5 w-3.5" aria-hidden /> {p.contactoEntrega}</p>}
                      {p.enderecoEntrega && (
                        <p className="mt-1 flex items-start gap-1 text-sm"><MapPin className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden /> {p.enderecoEntrega}</p>
                      )}
                      <p className="mt-1 text-sm font-medium tabular-nums">{formatarMoeda(p.total)} · {p.numeroItens} itens</p>
                      <div className="mt-2 flex flex-wrap gap-2">
                        {proximos(p).map((n) => (
                          <button key={n.estado} type="button" className="btn-primario min-h-10 flex-1 text-xs" disabled={!podeGravar || aGravar !== null}
                            onClick={() => void avancar(p, n.estado)}>{n.texto}</button>
                        ))}
                        {tem('pos.usar') && (
                          <button type="button" className="btn-secundario min-h-10 text-xs" onClick={() => navegar(`/pos?pedido=${p.id}`)}>Abrir no POS</button>
                        )}
                      </div>
                    </article>
                  ))}
                </div>
              </section>
            )
          })}
        </div>
      )}
    </div>
  )
}

