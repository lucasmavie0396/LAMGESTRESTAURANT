import { useState } from 'react'
import { Loader2, Pencil, Plus, Power, Search, Trash2, X } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Artigo, GrupoAcompanhamentos, Pagina, Produto, Unidade } from '../../types/api'
import { n3 } from '../stock/comum'
import { OpcoesArtigos } from '../stock/OpcoesArtigos'

/** Texto da regra de escolha (ex.: «escolher 1», «até 2», «1 a 2»). */
function regraEscolha(minimo: number, maximo: number) {
  if (minimo === maximo) return `escolher ${minimo}`
  if (minimo === 0) return `opcional, até ${maximo}`
  return `escolher ${minimo} a ${maximo}`
}

export function Acompanhamentos({ podeGerir }: { podeGerir: boolean }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const { dados: grupos, erro, recarregar } = useDados(() => api.get<GrupoAcompanhamentos[]>('/api/acompanhamentos?incluirInactivos=true'), [])
  const [editar, setEditar] = useState<GrupoAcompanhamentos | 'novo' | null>(null)

  const alterarActivo = async (g: GrupoAcompanhamentos) => {
    try {
      await api.post(`/api/acompanhamentos/${g.id}/activo`, { activo: !g.activo })
      avisos.sucesso(g.activo ? 'Grupo desactivado.' : 'Grupo activado.')
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="cartao">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 p-4">
        <p className="max-w-3xl text-sm text-slate-600">
          Acompanhamentos que o cliente escolhe com o prato (ex.: <strong>Arroz</strong>, <strong>Batata frita</strong>, <strong>Salada fria</strong>).
          No POS, ao tocar no prato, o empregado escolhe-os; saem no talão da cozinha com o prato, gastam o stock dos artigos indicados
          e <strong>não mudam o preço</strong>. Não aparecem à venda.
        </p>
        {podeGerir && (
          <button type="button" className="btn-primario" onClick={() => setEditar('novo')} disabled={!podeGravar}>
            <Plus className="h-4 w-4" aria-hidden /> Novo grupo
          </button>
        )}
      </div>
      {erro && <div className="p-4"><Alerta tipo="erro">{erro}</Alerta></div>}
      {!grupos && !erro && <Carregando />}
      {grupos?.length === 0 && <Vazio texto="Ainda não há grupos de acompanhamentos." />}
      {grupos && grupos.length > 0 && (
        <ul className="divide-y divide-slate-100">
          {grupos.map((g) => (
            <li key={g.id} className={`flex min-h-14 items-center gap-3 px-4 py-2 ${g.activo ? '' : 'opacity-60'}`}>
              <div className="min-w-0 flex-1">
                <p className="font-semibold">{g.nome} <span className="text-sm font-normal text-slate-500">({regraEscolha(g.minimo, g.maximo)})</span></p>
                <p className="text-sm text-slate-700">{g.opcoes.map((o) => o.nome).join(' · ')}</p>
                <p className="text-xs text-slate-500">
                  {g.produtos.length === 0 ? 'Ainda não está em nenhum prato.' : `Pratos: ${g.produtos.map((p) => p.nome).join(', ')}`}
                </p>
              </div>
              {!g.activo && <Etiqueta cor="cinza">Inactivo</Etiqueta>}
              {podeGerir && (
                <>
                  <button type="button" className="btn-icone" aria-label={`Editar ${g.nome}`} title="Editar" disabled={!podeGravar} onClick={() => setEditar(g)}>
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button type="button" className="btn-icone" aria-label={`${g.activo ? 'Desactivar' : 'Activar'} ${g.nome}`} title={g.activo ? 'Desactivar' : 'Activar'}
                    disabled={!podeGravar} onClick={() => void alterarActivo(g)}>
                    <Power className={`h-4 w-4 ${g.activo ? 'text-red-600' : 'text-green-700'}`} />
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
      {editar && (
        <EditorGrupo
          grupo={editar === 'novo' ? undefined : editar}
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

type LinhaOpcao = { artigoId: number; quantidade: string; unidadeId: number }
type Opcao = { id: number | null; nome: string; linhas: LinhaOpcao[] }

function EditorGrupo({ grupo, aoFechar, aoGuardar }: { grupo?: GrupoAcompanhamentos; aoFechar: () => void; aoGuardar: () => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const artigos = useDados(() => api.get<Pagina<Artigo>>('/api/stock/artigos?activo=true&tamanho=200'), [])
  const unidades = useDados(() => api.get<Unidade[]>('/api/stock/unidades'), [])
  const [nome, setNome] = useState(grupo?.nome ?? 'Acompanhamento')
  const [minimo, setMinimo] = useState(grupo?.minimo ?? 1)
  const [maximo, setMaximo] = useState(grupo?.maximo ?? 1)
  const [opcoes, setOpcoes] = useState<Opcao[]>(
    grupo?.opcoes.map((o) => ({ id: o.id, nome: o.nome, linhas: o.linhas.map((l) => ({ artigoId: l.artigoId, quantidade: String(l.quantidade), unidadeId: l.unidadeMedidaId })) }))
      ?? [{ id: null, nome: '', linhas: [] }],
  )
  const [pratos, setPratos] = useState<{ id: number; nome: string }[]>(grupo?.produtos ?? [])
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa, 300)
  const encontrados = useDados(
    () => (pesquisaAtrasada.trim().length < 2 ? Promise.resolve(null) : api.get<Pagina<Produto>>('/api/produtos' + query({ pesquisa: pesquisaAtrasada.trim(), activo: true, tamanho: 20 }))),
    [pesquisaAtrasada],
  )
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const listaArtigos = artigos.dados?.itens ?? []
  const unidadesDe = (artigoId: number) => {
    const a = listaArtigos.find((x) => x.id === artigoId)
    return a ? (unidades.dados ?? []).filter((u) => u.dimensao === a.dimensao) : []
  }
  const alterarOpcao = (i: number, o: Partial<Opcao>) => setOpcoes((os) => os.map((x, j) => (j === i ? { ...x, ...o } : x)))
  const alterarLinha = (i: number, k: number, l: Partial<LinhaOpcao>) =>
    setOpcoes((os) => os.map((x, j) => (j === i ? { ...x, linhas: x.linhas.map((y, m) => (m === k ? { ...y, ...l } : y)) } : x)))

  const guardar = async () => {
    const validas = opcoes.filter((o) => o.nome.trim())
    if (!nome.trim()) return setErro('Indique o nome do grupo.')
    if (validas.length === 0) return setErro('Indique pelo menos uma opção (ex.: Arroz).')
    if (maximo < 1 || minimo < 0 || maximo < minimo) return setErro('O máximo tem de ser pelo menos 1 e não pode ser menor que o mínimo.')
    if (validas.some((o) => o.linhas.some((l) => !l.artigoId || !(Number(l.quantidade) > 0) || !l.unidadeId)))
      return setErro('Cada artigo de uma opção precisa de quantidade positiva e unidade.')
    setErro(null)
    setAGuardar(true)
    const corpo = {
      nome: nome.trim(),
      minimo,
      maximo,
      opcoes: validas.map((o) => ({
        id: o.id,
        nome: o.nome.trim(),
        linhas: o.linhas.map((l) => ({ artigoId: l.artigoId, quantidade: Number(l.quantidade), unidadeMedidaId: l.unidadeId })),
      })),
      produtoIds: pratos.map((p) => p.id),
    }
    try {
      if (grupo) await api.put(`/api/acompanhamentos/${grupo.id}`, corpo)
      else await api.post('/api/acompanhamentos', corpo)
      avisos.sucesso(grupo ? 'Grupo de acompanhamentos actualizado.' : 'Grupo de acompanhamentos criado.')
      aoGuardar()
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal
      titulo={grupo ? `Editar ${grupo.nome}` : 'Novo grupo de acompanhamentos'}
      aoFechar={aoFechar}
      largura="max-w-4xl"
      rodape={
        <>
          <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
            {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
          </button>
        </>
      }
    >
      <div className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        {(artigos.erro || unidades.erro) && <Alerta tipo="aviso">Sem acesso aos artigos de stock: as opções podem ser criadas, mas sem gastar stock.</Alerta>}
        <div className="grid gap-4 sm:grid-cols-[1fr_8rem_8rem]">
          <div>
            <label htmlFor="nomeGrupo" className="rotulo">Nome do grupo</label>
            <input id="nomeGrupo" className="campo" value={nome} maxLength={100} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: Acompanhamento, Molho" />
          </div>
          <div>
            <label htmlFor="minimoGrupo" className="rotulo">Mínimo</label>
            <input id="minimoGrupo" type="number" min={0} max={20} className="campo" value={minimo} onChange={(e) => setMinimo(Math.max(0, Number(e.target.value) || 0))} />
          </div>
          <div>
            <label htmlFor="maximoGrupo" className="rotulo">Máximo</label>
            <input id="maximoGrupo" type="number" min={1} max={20} className="campo" value={maximo} onChange={(e) => setMaximo(Math.max(1, Number(e.target.value) || 1))} />
          </div>
        </div>
        <p className="text-xs text-slate-500">
          No POS: {regraEscolha(minimo, Math.max(minimo, maximo))}. Mínimo 0 = o empregado pode não escolher nenhum.
        </p>

        <fieldset className="space-y-3">
          <legend className="rotulo">Opções</legend>
          {opcoes.map((o, i) => (
            <div key={i} className="rounded-lg border border-slate-200 p-3">
              <div className="flex gap-2">
                <input className="campo" value={o.nome} maxLength={100} aria-label={`Opção ${i + 1}`} placeholder="Ex.: Arroz, Batata frita, Salada fria"
                  onChange={(e) => alterarOpcao(i, { nome: e.target.value })} />
                <button type="button" className="btn-icone" aria-label={`Remover opção ${i + 1}`} onClick={() => setOpcoes((os) => os.filter((_, j) => j !== i))}>
                  <Trash2 className="h-4 w-4 text-red-600" />
                </button>
              </div>
              <div className="mt-2 space-y-2 pl-2">
                {o.linhas.map((l, k) => (
                  <div key={k} className="grid grid-cols-[1fr_7rem_6rem_auto] items-center gap-2">
                    <select className="campo" value={l.artigoId || ''} aria-label={`Artigo ${k + 1} de ${o.nome || `opção ${i + 1}`}`}
                      onChange={(e) => {
                        const a = listaArtigos.find((x) => x.id === Number(e.target.value))
                        alterarLinha(i, k, { artigoId: a?.id ?? 0, unidadeId: a?.unidadeMedidaId ?? 0 })
                      }}>
                      <option value="">Artigo de stock…</option>
                      <OpcoesArtigos artigos={listaArtigos} />
                    </select>
                    <input type="number" min={0} step="0.001" className="campo" value={l.quantidade} aria-label={`Quantidade ${k + 1} de ${o.nome || `opção ${i + 1}`}`}
                      onChange={(e) => alterarLinha(i, k, { quantidade: e.target.value })} />
                    <select className="campo" value={l.unidadeId} disabled={!l.artigoId} aria-label={`Unidade ${k + 1} de ${o.nome || `opção ${i + 1}`}`}
                      onChange={(e) => alterarLinha(i, k, { unidadeId: Number(e.target.value) })}>
                      {unidadesDe(l.artigoId).map((u) => <option key={u.id} value={u.id}>{u.codigo}</option>)}
                    </select>
                    <button type="button" className="btn-icone" aria-label={`Retirar artigo ${k + 1}`} onClick={() => alterarOpcao(i, { linhas: o.linhas.filter((_, m) => m !== k) })}>
                      <X className="h-4 w-4" />
                    </button>
                  </div>
                ))}
                <button type="button" className="btn-secundario min-h-9 px-2 text-xs" disabled={listaArtigos.length === 0}
                  onClick={() => alterarOpcao(i, { linhas: [...o.linhas, { artigoId: 0, quantidade: '', unidadeId: 0 }] })}>
                  <Plus className="h-3.5 w-3.5" aria-hidden /> Artigo gasto por prato
                </button>
                {o.linhas.length === 0 && <span className="ml-2 text-xs text-slate-500">Sem artigos: sai no talão mas não gasta stock.</span>}
              </div>
            </div>
          ))}
          <button type="button" className="btn-secundario" onClick={() => setOpcoes((os) => [...os, { id: null, nome: '', linhas: [] }])}>
            <Plus className="h-4 w-4" aria-hidden /> Opção
          </button>
          <p className="text-xs text-slate-500">Quantidades por <strong>1 prato</strong> (ex.: Arroz = 0,150 kg de arroz). Com 2 pratos, gasta o dobro.</p>
        </fieldset>

        <fieldset>
          <legend className="rotulo">Pratos que pedem estes acompanhamentos</legend>
          <div className="mb-2 flex flex-wrap gap-2">
            {pratos.length === 0 && <span className="text-sm text-slate-500">Nenhum prato escolhido.</span>}
            {pratos.map((p) => (
              <span key={p.id} className="inline-flex items-center gap-1 rounded-full bg-primaria-50 px-3 py-1 text-sm text-primaria-800">
                {p.nome}
                <button type="button" aria-label={`Retirar ${p.nome}`} onClick={() => setPratos((ps) => ps.filter((x) => x.id !== p.id))}>
                  <X className="h-3.5 w-3.5" />
                </button>
              </span>
            ))}
          </div>
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
            <input className="campo pl-9" placeholder="Procurar prato para acrescentar…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Procurar prato" />
          </div>
          {encontrados.dados && (
            <ul className="mt-1 max-h-48 overflow-auto rounded-lg border border-slate-200">
              {encontrados.dados.itens.length === 0 && <li className="p-2 text-sm text-slate-500">Nenhum produto encontrado.</li>}
              {encontrados.dados.itens.map((p) => {
                const escolhido = pratos.some((x) => x.id === p.id)
                return (
                  <li key={p.id}>
                    <button type="button" className="flex min-h-10 w-full items-center justify-between px-3 text-left text-sm hover:bg-slate-50 disabled:opacity-50"
                      disabled={escolhido} onClick={() => setPratos((ps) => [...ps, { id: p.id, nome: p.nome }])}>
                      <span>{p.nome} <span className="text-slate-500">· {p.categoriaNome}</span></span>
                      <span className="text-xs text-primaria-700">{escolhido ? 'já está' : 'Acrescentar'}</span>
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </fieldset>
        {grupo && grupo.opcoes.some((o) => o.linhas.length > 0) && (
          <p className="text-xs text-slate-500">
            Actual: {grupo.opcoes.map((o) => `${o.nome}${o.linhas.length ? ` (${o.linhas.map((l) => `${n3.format(l.quantidade)} ${l.unidade} ${l.artigo}`).join(', ')})` : ''}`).join(' · ')}
          </p>
        )}
      </div>
    </Modal>
  )
}
