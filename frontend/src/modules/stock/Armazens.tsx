import { useState } from 'react'
import { ArrowRight, Loader2, Pencil, Plus, Power, Star, Trash2 } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Armazem, Artigo, EstacaoPreparacao, MovimentoStock, Pagina } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'
import { n3 } from './comum'

/** Stock → Armazéns: registo dos armazéns, de onde sai o stock de cada centro de produção e transferências entre armazéns. */
export function Armazens({ armazens, aoAlterar }: { armazens: Armazem[]; aoAlterar: () => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [editar, setEditar] = useState<Armazem | 'novo' | null>(null)
  const podeGerir = tem('stock.gerir')

  const accao = async (fazer: () => Promise<unknown>, ok: string) => {
    try {
      await fazer()
      avisos.sucesso(ok)
      aoAlterar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="space-y-4">
      <section className="cartao p-4">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="font-semibold">Armazéns</h2>
          {podeGerir && (
            <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setEditar('novo')}>
              <Plus className="h-4 w-4" aria-hidden /> Novo armazém
            </button>
          )}
        </div>
        <ul className="divide-y divide-slate-100">
          {armazens.map((a) => (
            <li key={a.id} className={`flex min-h-12 items-center gap-3 ${a.activo ? '' : 'opacity-60'}`}>
              <span className="flex-1 font-medium">{a.nome}</span>
              {a.principal && <Etiqueta cor="azul">Principal</Etiqueta>}
              {!a.activo && <Etiqueta cor="cinza">Inactivo</Etiqueta>}
              {podeGerir && (
                <div className="flex gap-1">
                  {a.activo && !a.principal && (
                    <button type="button" className="btn-icone" title="Tornar principal" aria-label={`Tornar ${a.nome} principal`} disabled={!podeGravar}
                      onClick={() => void accao(() => api.post(`/api/stock/armazens/${a.id}/principal`, {}), `${a.nome} é agora o armazém principal.`)}>
                      <Star className="h-4 w-4" />
                    </button>
                  )}
                  <button type="button" className="btn-icone" aria-label={`Editar ${a.nome}`} disabled={!podeGravar} onClick={() => setEditar(a)}><Pencil className="h-4 w-4" /></button>
                  {!a.principal && (
                    <button type="button" className="btn-icone" aria-label={`${a.activo ? 'Desactivar' : 'Activar'} ${a.nome}`} disabled={!podeGravar}
                      onClick={() => void accao(() => api.post(`/api/stock/armazens/${a.id}/activo`, { activo: !a.activo }), a.activo ? 'Armazém desactivado.' : 'Armazém activado.')}>
                      <Power className={`h-4 w-4 ${a.activo ? 'text-red-600' : 'text-green-700'}`} />
                    </button>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      </section>

      <ArmazemPorCentro armazens={armazens} podeGerir={podeGerir} />
      {podeGerir && <Transferencia armazens={armazens} aoTransferir={aoAlterar} />}
      {editar && <FormArmazem armazem={editar === 'novo' ? null : editar} aoFechar={(gravou) => { setEditar(null); if (gravou) aoAlterar() }} />}
    </div>
  )
}

function FormArmazem({ armazem, aoFechar }: { armazem: Armazem | null; aoFechar: (gravou: boolean) => void }) {
  const avisos = useAvisos()
  const [nome, setNome] = useState(armazem?.nome ?? '')
  const [aGuardar, setAGuardar] = useState(false)
  const guardar = async () => {
    setAGuardar(true)
    try {
      if (armazem) await api.put(`/api/stock/armazens/${armazem.id}`, { nome: nome.trim() })
      else await api.post('/api/stock/armazens', { nome: nome.trim() })
      avisos.sucesso(armazem ? 'Armazém actualizado.' : 'Armazém criado.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }
  return (
    <Modal titulo={armazem ? `Editar ${armazem.nome}` : 'Novo armazém'} aoFechar={() => aoFechar(false)}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar(false)}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aGuardar || !nome.trim()} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <label htmlFor="nomeArmazem" className="rotulo">Nome</label>
      <input id="nomeArmazem" className="campo" maxLength={200} value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: Bar, Câmara fria" />
    </Modal>
  )
}

/** De que armazém sai o stock vendido por cada centro de produção. */
function ArmazemPorCentro({ armazens, podeGerir }: { armazens: Armazem[]; podeGerir: boolean }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const estacoes = useDados(() => api.get<EstacaoPreparacao[]>('/api/catalogo/estacoes'), [])

  const definir = async (e: EstacaoPreparacao, valor: string) => {
    try {
      await api.put(`/api/catalogo/estacoes/${e.id}/armazem`, { armazemId: valor ? Number(valor) : null })
      avisos.sucesso(`Armazém de ${e.nome} actualizado.`)
      await estacoes.recarregar()
    } catch (erro) {
      avisos.erro(mensagemErro(erro))
    }
  }

  return (
    <section className="cartao p-4">
      <h2 className="font-semibold">Armazém de abate por centro de produção</h2>
      <p className="mb-3 max-w-3xl text-sm text-slate-600">
        A venda baixa o stock no armazém do centro de produção da categoria do produto. Sem centro ou sem armazém definido, usa o armazém registado no produto e, na falta dele, o principal.
      </p>
      {estacoes.erro && <Alerta tipo="erro">{estacoes.erro}</Alerta>}
      {!estacoes.dados && !estacoes.erro && <Carregando />}
      {estacoes.dados?.length === 0 && <p className="text-sm text-slate-500">Ainda não há centros de produção. Criam-se em Produtos → Categorias.</p>}
      <div className="grid gap-3 sm:grid-cols-2">
        {estacoes.dados?.filter((e) => e.activo).map((e) => (
          <div key={e.id} className="flex items-center justify-between gap-3">
            <label htmlFor={`centro-${e.id}`} className="text-sm font-medium">{e.nome}</label>
            <select id={`centro-${e.id}`} className="campo max-w-56" value={e.armazemId ?? ''} disabled={!podeGerir || !podeGravar} onChange={(x) => void definir(e, x.target.value)}>
              <option value="">Armazém do produto</option>
              {armazens.filter((a) => a.activo || a.id === e.armazemId).map((a) => <option key={a.id} value={a.id}>{a.nome}</option>)}
            </select>
          </div>
        ))}
      </div>
    </section>
  )
}

interface LinhaTransferir { artigo: Artigo; quantidade: string }

/** Vários artigos de um armazém para outro, numa só operação. */
function Transferencia({ armazens, aoTransferir }: { armazens: Armazem[]; aoTransferir: () => void }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const activos = armazens.filter((a) => a.activo)
  const [origem, setOrigem] = useState(activos.find((a) => a.principal)?.id ?? activos[0]?.id ?? 0)
  const [destino, setDestino] = useState(activos.find((a) => !a.principal)?.id ?? 0)
  const [linhas, setLinhas] = useState<LinhaTransferir[]>([])
  const [motivo, setMotivo] = useState('')
  const [pesquisa, setPesquisa] = useState('')
  const [aGuardar, setAGuardar] = useState(false)
  const atrasada = useAtraso(pesquisa)
  const resultados = useDados(
    () => (atrasada.trim() ? api.get<Pagina<Artigo>>('/api/stock/artigos' + query({ pesquisa: atrasada.trim(), activo: true, tamanho: 8 })) : Promise.resolve(null)),
    [atrasada],
  )
  const recentes = useDados(() => api.get<Pagina<MovimentoStock>>('/api/stock/movimentos?tipo=TransferenciaSaida&tamanho=10'), [])

  const saldo = (a: Artigo, armazem: number) => a.porArmazem.find((s) => s.armazemId === armazem)?.quantidade ?? 0
  const valor = (l: LinhaTransferir) => Number(l.quantidade.replace(',', '.'))
  const linhaInvalida = (l: LinhaTransferir) => !(valor(l) > 0) || valor(l) > saldo(l.artigo, origem)
  const pronto = linhas.length > 0 && !linhas.some(linhaInvalida) && origem > 0 && destino > 0 && origem !== destino

  const juntar = (a: Artigo) => {
    if (!linhas.some((l) => l.artigo.id === a.id)) setLinhas((x) => [...x, { artigo: a, quantidade: '' }])
    setPesquisa('')
  }

  const transferir = async () => {
    setAGuardar(true)
    try {
      await api.post('/api/stock/transferencias/varios', {
        armazemOrigemId: origem, armazemDestinoId: destino, motivo: motivo.trim() || null,
        linhas: linhas.map((l) => ({ artigoId: l.artigo.id, quantidade: valor(l) })),
      })
      avisos.sucesso('Transferência registada.')
      setLinhas([])
      setMotivo('')
      await recentes.recarregar()
      aoTransferir()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  if (activos.length < 2) {
    return (
      <section className="cartao p-4">
        <h2 className="font-semibold">Transferência entre armazéns</h2>
        <p className="text-sm text-slate-500">É preciso ter pelo menos dois armazéns activos.</p>
      </section>
    )
  }

  return (
    <section className="cartao space-y-3 p-4">
      <h2 className="font-semibold">Transferência entre armazéns</h2>
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <label htmlFor="trOrigem" className="rotulo">De</label>
          <select id="trOrigem" className="campo min-w-48" value={origem} onChange={(e) => setOrigem(Number(e.target.value))}>
            {activos.map((a) => <option key={a.id} value={a.id}>{a.nome}</option>)}
          </select>
        </div>
        <ArrowRight className="mb-3 h-4 w-4 text-slate-400" aria-hidden />
        <div>
          <label htmlFor="trDestino" className="rotulo">Para</label>
          <select id="trDestino" className="campo min-w-48" value={destino} onChange={(e) => setDestino(Number(e.target.value))}>
            <option value={0}>Escolha…</option>
            {activos.map((a) => <option key={a.id} value={a.id}>{a.nome}</option>)}
          </select>
        </div>
      </div>
      {origem === destino && <p className="text-sm text-red-700">Os armazéns de origem e destino têm de ser diferentes.</p>}

      <div className="relative max-w-md">
        <label htmlFor="trPesquisa" className="rotulo">Adicionar artigo</label>
        <input id="trPesquisa" type="search" className="campo" placeholder="Nome ou código…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} />
        {pesquisa.trim() && resultados.dados && (
          <ul className="absolute z-10 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white shadow-lg">
            {resultados.dados.itens.length === 0 && <li className="p-3 text-sm text-slate-500">Sem resultados.</li>}
            {resultados.dados.itens.map((a) => (
              <li key={a.id}>
                <button type="button" className="flex min-h-11 w-full items-center justify-between gap-3 px-3 text-left text-sm hover:bg-slate-50" onClick={() => juntar(a)}>
                  <span>{a.nome}</span>
                  <span className="whitespace-nowrap text-slate-500">{n3.format(saldo(a, origem))} {a.unidade}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {linhas.length > 0 && (
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
          {linhas.map((l) => (
            <li key={l.artigo.id} className="flex flex-wrap items-center gap-3 px-3 py-2">
              <span className="min-w-40 flex-1 text-sm font-medium">{l.artigo.nome}</span>
              <span className="text-xs text-slate-500">Na origem: {n3.format(saldo(l.artigo, origem))} {l.artigo.unidade}</span>
              <input className="campo w-28 text-right" inputMode="decimal" aria-label={`Quantidade de ${l.artigo.nome}`} aria-invalid={l.quantidade !== '' && linhaInvalida(l)} value={l.quantidade}
                onChange={(e) => setLinhas((x) => x.map((y) => (y.artigo.id === l.artigo.id ? { ...y, quantidade: e.target.value.trim() } : y)))} />
              <button type="button" className="btn-icone" aria-label={`Retirar ${l.artigo.nome}`} onClick={() => setLinhas((x) => x.filter((y) => y.artigo.id !== l.artigo.id))}>
                <Trash2 className="h-4 w-4" />
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-56 flex-1">
          <label htmlFor="trMotivo" className="rotulo">Motivo (opcional)</label>
          <input id="trMotivo" className="campo" maxLength={500} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </div>
        <button type="button" className="btn-primario" disabled={!pronto || aGuardar || !podeGravar} onClick={() => void transferir()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Transferir
        </button>
      </div>

      {!!recentes.dados?.itens.length && (
        <div>
          <h3 className="mb-1 text-sm font-semibold">Últimas transferências</h3>
          <ul className="divide-y divide-slate-100 text-sm">
            {recentes.dados.itens.map((m) => (
              <li key={m.id} className="flex flex-wrap justify-between gap-2 py-1.5">
                <span>{m.artigo} · <span className="tabular-nums">{n3.format(Math.abs(m.quantidade))} {m.unidade}</span></span>
                <span className="text-slate-500">{m.motivo} · {formatarDataHora(m.dataUtc)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  )
}
