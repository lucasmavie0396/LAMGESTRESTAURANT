import { useState } from 'react'
import { api, query } from '../../api/cliente'
import { Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAtraso } from '../../hooks/useAtraso'
import { useDados } from '../../hooks/useDados'
import type { Pagina, PedidoResumo } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { nomeTipo } from './tiposPos'

/** Pedidos abertos e recentes (para reimprimir recibos ou estornar). */
export function ListaPedidos({ abertos, aoAbrir, aoFechar }: { abertos: PedidoResumo[]; aoAbrir: (id: number) => void; aoFechar: () => void }) {
  const [separador, setSeparador] = useState<'abertos' | 'recentes'>('abertos')
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const recentes = useDados(
    () => (separador === 'recentes' ? api.get<Pagina<PedidoResumo>>('/api/pedidos' + query({ pesquisa: pesquisaAtrasada, tamanho: 50 })) : Promise.resolve(null)),
    [separador, pesquisaAtrasada],
  )
  const lista = separador === 'abertos' ? abertos : (recentes.dados?.itens ?? [])
  const cor = { Aberto: 'azul', Pago: 'verde', Cancelado: 'cinza', Estornado: 'vermelho' } as const

  return (
    <Modal titulo="Pedidos" aoFechar={aoFechar} largura="max-w-2xl">
      <div className="mb-3 flex flex-wrap gap-2">
        {(['abertos', 'recentes'] as const).map((s) => (
          <button key={s} type="button" onClick={() => setSeparador(s)} className={separador === s ? 'btn-primario' : 'btn-secundario'}>
            {s === 'abertos' ? `Abertos (${abertos.length})` : 'Recentes'}
          </button>
        ))}
        {separador === 'recentes' && (
          <input type="search" className="campo flex-1" placeholder="N.º do pedido, nome, contacto ou mesa…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar pedidos" />
        )}
      </div>
      {lista.length === 0 && <Vazio texto={separador === 'abertos' ? 'Não há pedidos abertos.' : 'Sem pedidos.'} />}
      <ul className="divide-y divide-slate-100">
        {lista.map((a) => (
              <li key={a.id}>
                <button type="button" className="flex min-h-14 w-full items-center gap-3 px-2 text-left hover:bg-slate-50" onClick={() => aoAbrir(a.id)}>
                  <span className="w-28 font-semibold">{nomeTipo(a.tipo, a.mesaNome)}</span>
                  <span className="flex-1 text-sm text-slate-600">{a.numero}{a.nomeEntrega && ` · ${a.nomeEntrega}`}{a.contactoEntrega && ` · ${a.contactoEntrega}`}</span>
                  {a.estado !== 'Aberto' && <Etiqueta cor={cor[a.estado]}>{a.estado}</Etiqueta>}
                  {a.estado === 'Aberto' && a.itensPorEnviar > 0 && <Etiqueta cor="cinza">{a.itensPorEnviar} por enviar</Etiqueta>}
                  {a.estado === 'Aberto' && a.itensProntos > 0 && <Etiqueta cor="verde">{a.itensProntos} prontos</Etiqueta>}
                  <span className="w-28 text-right font-semibold tabular-nums">{formatarMoeda(a.total)}</span>
                </button>
              </li>
        ))}
      </ul>
    </Modal>
  )
}
