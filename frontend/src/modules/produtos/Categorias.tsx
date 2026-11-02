import { useState } from 'react'
import { Loader2, Pencil, Plus, Power } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Categoria, EstacaoPreparacao } from '../../types/api'

export function Categorias({ podeGerir }: { podeGerir: boolean }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const { dados: categorias, erro, recarregar } = useDados(() => api.get<Categoria[]>('/api/categorias?incluirInactivas=true'), [])
  const { dados: estacoes, recarregar: recarregarEstacoes } = useDados(() => api.get<EstacaoPreparacao[]>('/api/catalogo/estacoes'), [])
  const [editar, setEditar] = useState<Categoria | 'nova' | null>(null)

  const principais = categorias?.filter((c) => !c.categoriaPaiId) ?? []
  const filhas = (id: number) => categorias?.filter((c) => c.categoriaPaiId === id) ?? []

  const alterarActiva = async (c: Categoria) => {
    try {
      await api.post(`/api/categorias/${c.id}/activo`, { activo: !c.activo })
      avisos.sucesso(c.activo ? 'Categoria desactivada.' : 'Categoria activada.')
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const Linha = ({ c, nivel }: { c: Categoria; nivel: number }) => (
    <li className={`flex min-h-12 items-center gap-3 px-4 ${nivel > 0 ? 'pl-12' : ''} ${c.activo ? '' : 'opacity-60'}`}>
      <span className="h-4 w-4 shrink-0 rounded" style={{ backgroundColor: c.cor ?? '#cbd5e1' }} aria-hidden />
      <div className="min-w-0 flex-1">
        <p className="font-medium">{c.nome}</p>
        <p className="text-xs text-slate-500">
          {c.numeroProdutosActivos} produtos activos{c.estacaoPreparacaoNome && ` · estação: ${c.estacaoPreparacaoNome}`}
        </p>
      </div>
      {!c.activo && <Etiqueta cor="cinza">Inactiva</Etiqueta>}
      {podeGerir && (
        <>
          <button type="button" className="btn-icone" aria-label={`Editar ${c.nome}`} title="Editar" disabled={!podeGravar} onClick={() => setEditar(c)}>
            <Pencil className="h-4 w-4" />
          </button>
          <button type="button" className="btn-icone" aria-label={`${c.activo ? 'Desactivar' : 'Activar'} ${c.nome}`} title={c.activo ? 'Desactivar' : 'Activar'}
            disabled={!podeGravar} onClick={() => void alterarActiva(c)}>
            <Power className={`h-4 w-4 ${c.activo ? 'text-red-600' : 'text-green-700'}`} />
          </button>
        </>
      )}
    </li>
  )

  return (
    <div className="cartao">
      <div className="flex items-center justify-between border-b border-slate-200 p-4">
        <p className="text-sm text-slate-600">As categorias organizam os botões do POS. Categorias com produtos activos não podem ser desactivadas.</p>
        {podeGerir && (
          <button type="button" className="btn-primario" onClick={() => setEditar('nova')} disabled={!podeGravar}>
            <Plus className="h-4 w-4" aria-hidden /> Nova categoria
          </button>
        )}
      </div>
      {erro && <div className="p-4"><Alerta tipo="erro">{erro}</Alerta></div>}
      {!categorias && !erro && <Carregando />}
      {categorias?.length === 0 && <Vazio texto="Ainda não há categorias." />}
      {categorias && categorias.length > 0 && (
        <ul className="divide-y divide-slate-100">
          {principais.map((c) => (
            <li key={c.id}>
              <ul className="divide-y divide-slate-100">
                <Linha c={c} nivel={0} />
                {filhas(c.id).map((f) => (
                  <Linha key={f.id} c={f} nivel={1} />
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}

      {editar && categorias && (
        <EditorCategoria
          categoria={editar === 'nova' ? undefined : editar}
          principais={principais.filter((p) => p.activo && (editar === 'nova' || p.id !== editar.id))}
          estacoes={estacoes ?? []}
          aoCriarEstacao={recarregarEstacoes}
          aoFechar={() => setEditar(null)}
          aoGuardar={async () => {
            setEditar(null)
            await recarregar()
          }}
        />
      )}
    </div>
  )
}

function EditorCategoria({
  categoria,
  principais,
  estacoes,
  aoCriarEstacao,
  aoFechar,
  aoGuardar,
}: {
  categoria?: Categoria
  principais: Categoria[]
  estacoes: EstacaoPreparacao[]
  aoCriarEstacao: () => Promise<void>
  aoFechar: () => void
  aoGuardar: () => void
}) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [nome, setNome] = useState(categoria?.nome ?? '')
  const [paiId, setPaiId] = useState(categoria?.categoriaPaiId?.toString() ?? '')
  const [estacaoId, setEstacaoId] = useState(categoria?.estacaoPreparacaoId?.toString() ?? '')
  const [novaEstacao, setNovaEstacao] = useState('')
  const [cor, setCor] = useState(categoria?.cor ?? '#2563EB')
  const [ordem, setOrdem] = useState(categoria?.ordem ?? 0)
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const criarEstacao = async () => {
    try {
      const e = await api.post<EstacaoPreparacao>('/api/catalogo/estacoes', { nome: novaEstacao })
      await aoCriarEstacao()
      setEstacaoId(e.id.toString())
      setNovaEstacao('')
    } catch (e) {
      setErro(mensagemErro(e))
    }
  }

  const guardar = async () => {
    if (!nome.trim()) {
      setErro('Indique o nome da categoria.')
      return
    }
    setErro(null)
    setAGuardar(true)
    const corpo = {
      nome: nome.trim(),
      descricao: null,
      categoriaPaiId: paiId ? Number(paiId) : null,
      estacaoPreparacaoId: estacaoId ? Number(estacaoId) : null,
      cor,
      ordem,
    }
    try {
      if (categoria) await api.put(`/api/categorias/${categoria.id}`, corpo)
      else await api.post('/api/categorias', corpo)
      avisos.sucesso(categoria ? 'Categoria actualizada.' : 'Categoria criada.')
      aoGuardar()
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível guardar a categoria.')
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal
      titulo={categoria ? `Editar ${categoria.nome}` : 'Nova categoria'}
      aoFechar={aoFechar}
      rodape={
        <>
          <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
            {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
            Guardar
          </button>
        </>
      }
    >
      <div className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div>
          <label htmlFor="nomeCategoria" className="rotulo">Nome</label>
          <input id="nomeCategoria" className="campo" value={nome} maxLength={100} onChange={(e) => setNome(e.target.value)} />
        </div>
        <div>
          <label htmlFor="paiCategoria" className="rotulo">Categoria principal</label>
          <select id="paiCategoria" className="campo" value={paiId} onChange={(e) => setPaiId(e.target.value)}>
            <option value="">— Nenhuma (categoria principal) —</option>
            {principais.map((p) => (
              <option key={p.id} value={p.id}>{p.nome}</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="estacaoCategoria" className="rotulo">Estação de preparação (filtro do ecrã da cozinha)</label>
          <select id="estacaoCategoria" className="campo" value={estacaoId} onChange={(e) => setEstacaoId(e.target.value)}>
            <option value="">— Nenhuma —</option>
            {estacoes.filter((e) => e.activo).map((e) => (
              <option key={e.id} value={e.id}>{e.nome}</option>
            ))}
          </select>
          <div className="mt-2 flex gap-2">
            <input className="campo" placeholder="Nova estação (ex.: Bar, Grelhados)" value={novaEstacao} onChange={(e) => setNovaEstacao(e.target.value)} aria-label="Nome da nova estação" />
            <button type="button" className="btn-secundario" onClick={() => void criarEstacao()} disabled={novaEstacao.trim().length < 2 || !podeGravar}>
              Criar
            </button>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label htmlFor="corCategoria" className="rotulo">Cor no POS</label>
            <input id="corCategoria" type="color" className="campo h-11 p-1" value={cor} onChange={(e) => setCor(e.target.value.toUpperCase())} />
          </div>
          <div>
            <label htmlFor="ordemCategoria" className="rotulo">Ordem</label>
            <input id="ordemCategoria" type="number" min={0} max={9999} className="campo" value={ordem} onChange={(e) => setOrdem(Number(e.target.value) || 0)} />
          </div>
        </div>
      </div>
    </Modal>
  )
}
