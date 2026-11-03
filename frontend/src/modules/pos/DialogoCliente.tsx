import { useState } from 'react'
import { Plus, Search, UserMinus } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Modal, Vazio } from '../../components/ui'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { useDados } from '../../hooks/useDados'
import type { Cliente, Pagina, Pedido } from '../../types/api'
import { FormCliente } from '../clientes/PaginaClientes'

/** Procura (ou cria) o cliente e associa-o ao pedido aberto — necessário para acumular pontos de fidelização. */
export function DialogoCliente({ pedido, aoEscolher, aoFechar }: { pedido: Pedido; aoEscolher: (clienteId: number | null) => void; aoFechar: () => void }) {
  const { tem } = useSessao()
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const [novo, setNovo] = useState(false)
  const lista = useDados(
    () => (pesquisaAtrasada.trim().length >= 2 ? api.get<Pagina<Cliente>>('/api/clientes' + query({ pesquisa: pesquisaAtrasada, activo: true, tamanho: 20 })) : Promise.resolve(null)),
    [pesquisaAtrasada],
  )

  if (novo) return <FormCliente aoFechar={(c) => (c ? aoEscolher(c.id) : setNovo(false))} />

  return (
    <Modal titulo="Cliente do pedido" aoFechar={aoFechar} largura="max-w-xl"
      rodape={<>
        {pedido.clienteId && <button type="button" className="btn-secundario mr-auto text-red-700" onClick={() => aoEscolher(null)}><UserMinus className="h-4 w-4" aria-hidden /> Retirar {pedido.clienteNome}</button>}
        {tem('clientes.gerir') && <button type="button" className="btn-secundario" onClick={() => setNovo(true)}><Plus className="h-4 w-4" aria-hidden /> Novo cliente</button>}
        <button type="button" className="btn-secundario" onClick={aoFechar}>Fechar</button>
      </>}>
      <div className="relative">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
        <input type="search" className="campo pl-9" placeholder="Telefone, nome ou n.º do cartão…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar cliente" />
      </div>
      <div className="mt-3">
        {pesquisaAtrasada.trim().length < 2 && <p className="text-sm text-slate-500">Escreva pelo menos 2 caracteres.</p>}
        {lista.dados?.itens.length === 0 && <Vazio texto="Nenhum cliente encontrado." />}
        <ul className="divide-y divide-slate-100">
          {lista.dados?.itens.map((c) => (
            <li key={c.id}>
              <button type="button" className="flex min-h-14 w-full items-center gap-3 px-2 text-left hover:bg-slate-50" onClick={() => aoEscolher(c.id)}>
                <span className="flex-1">
                  <span className="font-medium">{c.nome}</span>
                  <span className="block text-xs text-slate-500">{[c.telefone, c.cartaoFidelidade && `cartão ${c.cartaoFidelidade}`].filter(Boolean).join(' · ')}</span>
                </span>
                <span className="text-sm text-slate-600">{c.saldoPontos} pts</span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    </Modal>
  )
}
