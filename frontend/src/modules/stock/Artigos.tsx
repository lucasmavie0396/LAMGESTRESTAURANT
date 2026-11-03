import { useState } from 'react'
import { ArrowLeftRight, History, Loader2, Pencil, Plus, Power, Scale, Search } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Armazem, Artigo, MovimentoStock, Pagina, TipoArtigo, Unidade } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { NOME_MOVIMENTO, NOME_TIPO_ARTIGO, n3, numero } from './comum'

const TAMANHO = 25
type Dialogo = { tipo: 'form'; artigo?: Artigo } | { tipo: 'ajuste'; artigo: Artigo } | { tipo: 'transferir'; artigo: Artigo } | { tipo: 'movimentos'; artigo: Artigo } | null

export function Artigos({ unidades, armazens }: { unidades: Unidade[]; armazens: Armazem[] }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const [filtro, setFiltro] = useState<'activos' | 'abaixo' | 'inactivos'>('activos')
  const [pagina, setPagina] = useState(1)
  const [dialogo, setDialogo] = useState<Dialogo>(null)

  const lista = useDados(
    () => api.get<Pagina<Artigo>>('/api/stock/artigos' + query({
      pesquisa: pesquisaAtrasada, activo: filtro !== 'inactivos', abaixoMinimo: filtro === 'abaixo', pagina, tamanho: TAMANHO,
    })),
    [pesquisaAtrasada, filtro, pagina],
  )

  const alternar = async (a: Artigo) => {
    try {
      await api.post(`/api/stock/artigos/${a.id}/activo`, { activo: !a.activo })
      avisos.sucesso(a.activo ? 'Artigo desactivado.' : 'Artigo activado.')
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }
  const fechar = async (recarregar = false) => {
    setDialogo(null)
    if (recarregar) await lista.recarregar()
  }

  return (
    <div className="cartao">
      <div className="flex flex-wrap items-center gap-3 border-b border-slate-200 p-4">
        <div className="relative min-w-60 flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input type="search" className="campo pl-9" placeholder="Pesquisar por nome ou código…" value={pesquisa}
            onChange={(e) => { setPesquisa(e.target.value); setPagina(1) }} aria-label="Pesquisar artigos" />
        </div>
        <select className="campo w-48" value={filtro} onChange={(e) => { setFiltro(e.target.value as typeof filtro); setPagina(1) }} aria-label="Filtro">
          <option value="activos">Activos</option>
          <option value="abaixo">Abaixo do mínimo</option>
          <option value="inactivos">Inactivos</option>
        </select>
        {tem('stock.gerir') && (
          <button type="button" className="btn-primario" onClick={() => setDialogo({ tipo: 'form' })} disabled={!podeGravar}>
            <Plus className="h-4 w-4" aria-hidden /> Novo artigo
          </button>
        )}
      </div>
      {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados?.itens.length === 0 && <Vazio texto={filtro === 'abaixo' ? 'Nenhum artigo abaixo do mínimo.' : 'Sem artigos.'} />}
      {lista.dados && lista.dados.itens.length > 0 && (
        <div className={`overflow-x-auto ${lista.aCarregar ? 'opacity-60' : ''}`}>
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-600">
              <tr>
                <th scope="col" className="px-4 py-3">Código</th>
                <th scope="col" className="px-4 py-3">Artigo</th>
                <th scope="col" className="px-4 py-3 text-right">Saldo</th>
                <th scope="col" className="px-4 py-3 text-right">Mínimo</th>
                <th scope="col" className="px-4 py-3 text-right">Custo médio</th>
                <th scope="col" className="px-4 py-3 text-right">Valor</th>
                <th scope="col" className="px-4 py-3 text-right">Acções</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {lista.dados.itens.map((a) => (
                <tr key={a.id} className="hover:bg-slate-50">
                  <td className="px-4 py-2 font-mono text-xs text-slate-600">{a.codigo}</td>
                  <td className="px-4 py-2">
                    <p className="font-medium">{a.nome}</p>
                    <p className="text-xs text-slate-500">{NOME_TIPO_ARTIGO[a.tipo]}{a.permiteStockNegativo && ' · pode ficar negativo'}{!a.activo && ' · inactivo'}</p>
                  </td>
                  <td className="px-4 py-2 text-right">
                    <span className={a.saldo < 0 ? 'font-semibold text-red-700' : ''}>{n3.format(a.saldo)} {a.unidade}</span>
                    {a.abaixoMinimo && <span className="ml-2"><Etiqueta cor="vermelho">Baixo</Etiqueta></span>}
                    {a.porArmazem.length > 1 && <p className="text-xs text-slate-500">{a.porArmazem.map((s) => `${s.armazem}: ${n3.format(s.quantidade)}`).join(' · ')}</p>}
                  </td>
                  <td className="px-4 py-2 text-right text-slate-600">{a.stockMinimo > 0 ? `${n3.format(a.stockMinimo)} ${a.unidade}` : '—'}</td>
                  <td className="px-4 py-2 text-right">{formatarMoeda(a.custoMedio)}<span className="text-xs text-slate-500"> /{a.unidade}</span></td>
                  <td className="px-4 py-2 text-right">{formatarMoeda(a.valorStock)}</td>
                  <td className="px-4 py-2">
                    <div className="flex justify-end">
                      <button type="button" className="btn-icone" title="Movimentos" aria-label={`Movimentos de ${a.nome}`} onClick={() => setDialogo({ tipo: 'movimentos', artigo: a })}>
                        <History className="h-4 w-4" />
                      </button>
                      {tem('stock.ajustar') && a.activo && (
                        <button type="button" className="btn-icone" title="Ajuste / perda" aria-label={`Ajustar ${a.nome}`} disabled={!podeGravar} onClick={() => setDialogo({ tipo: 'ajuste', artigo: a })}>
                          <Scale className="h-4 w-4" />
                        </button>
                      )}
                      {tem('stock.gerir') && a.activo && armazens.filter((x) => x.activo).length > 1 && (
                        <button type="button" className="btn-icone" title="Transferir" aria-label={`Transferir ${a.nome}`} disabled={!podeGravar} onClick={() => setDialogo({ tipo: 'transferir', artigo: a })}>
                          <ArrowLeftRight className="h-4 w-4" />
                        </button>
                      )}
                      {tem('stock.gerir') && (
                        <>
                          <button type="button" className="btn-icone" title="Editar" aria-label={`Editar ${a.nome}`} disabled={!podeGravar} onClick={() => setDialogo({ tipo: 'form', artigo: a })}>
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button type="button" className="btn-icone" title={a.activo ? 'Desactivar' : 'Activar'} aria-label={`${a.activo ? 'Desactivar' : 'Activar'} ${a.nome}`}
                            disabled={!podeGravar} onClick={() => void alternar(a)}>
                            <Power className={`h-4 w-4 ${a.activo ? 'text-red-600' : 'text-green-700'}`} />
                          </button>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {lista.dados && lista.dados.total > 0 && <Paginacao pagina={pagina} tamanho={TAMANHO} total={lista.dados.total} aoMudar={setPagina} />}

      {dialogo?.tipo === 'form' && <FormArtigo artigo={dialogo.artigo} unidades={unidades} aoFechar={(r) => void fechar(r)} />}
      {dialogo?.tipo === 'ajuste' && <AjusteModal artigo={dialogo.artigo} armazens={armazens} aoFechar={(r) => void fechar(r)} />}
      {dialogo?.tipo === 'transferir' && <TransferirModal artigo={dialogo.artigo} armazens={armazens} aoFechar={(r) => void fechar(r)} />}
      {dialogo?.tipo === 'movimentos' && (
        <Modal titulo={`Movimentos: ${dialogo.artigo.nome}`} aoFechar={() => setDialogo(null)} largura="max-w-4xl">
          <ListaMovimentos artigoId={dialogo.artigo.id} />
        </Modal>
      )}
    </div>
  )
}

function Rodape({ aoFechar, aoGuardar, aGuardar, texto = 'Guardar', desactivado }: { aoFechar: () => void; aoGuardar: () => void; aGuardar: boolean; texto?: string; desactivado?: boolean }) {
  const { podeGravar } = useLigacao()
  return (
    <>
      <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
      <button type="button" className="btn-primario" onClick={aoGuardar} disabled={aGuardar || !podeGravar || desactivado}>
        {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} {texto}
      </button>
    </>
  )
}

function FormArtigo({ artigo, unidades, aoFechar }: { artigo?: Artigo; unidades: Unidade[]; aoFechar: (recarregar?: boolean) => void }) {
  const { tem } = useSessao()
  const avisos = useAvisos()
  const [codigo, setCodigo] = useState(artigo?.codigo ?? '')
  const [nome, setNome] = useState(artigo?.nome ?? '')
  const [tipo, setTipo] = useState<TipoArtigo>(artigo?.tipo ?? 'Ingrediente')
  const [unidadeId, setUnidadeId] = useState(artigo?.unidadeMedidaId ?? unidades.find((u) => u.codigo === 'kg')?.id ?? 0)
  const [minimo, setMinimo] = useState(String(artigo?.stockMinimo ?? 0))
  const [negativo, setNegativo] = useState(artigo?.permiteStockNegativo ?? false)
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const guardar = async () => {
    if (!codigo.trim() || !nome.trim()) return setErro('Indique o código e o nome.')
    if (!(numero(minimo) >= 0)) return setErro('O stock mínimo tem de ser zero ou positivo.')
    setErro(null)
    setAGuardar(true)
    const corpo = { codigo: codigo.trim(), nome: nome.trim(), tipo, unidadeMedidaId: unidadeId, stockMinimo: numero(minimo), permiteStockNegativo: negativo }
    try {
      if (artigo) await api.put(`/api/stock/artigos/${artigo.id}`, corpo)
      else await api.post('/api/stock/artigos', corpo)
      avisos.sucesso(artigo ? 'Artigo actualizado.' : 'Artigo criado.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={artigo ? `Editar ${artigo.nome}` : 'Novo artigo'} aoFechar={() => aoFechar()}
      rodape={<Rodape aoFechar={() => aoFechar()} aoGuardar={() => void guardar()} aGuardar={aGuardar} />}>
      <div className="grid gap-4 sm:grid-cols-2">
        {erro && <div className="sm:col-span-2"><Alerta tipo="erro">{erro}</Alerta></div>}
        <div>
          <label htmlFor="codArt" className="rotulo">Código</label>
          <input id="codArt" className="campo" maxLength={30} value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="ING-FRANGO" />
        </div>
        <div>
          <label htmlFor="tipoArt" className="rotulo">Tipo</label>
          <select id="tipoArt" className="campo" value={tipo} onChange={(e) => setTipo(e.target.value as TipoArtigo)}>
            {(Object.keys(NOME_TIPO_ARTIGO) as TipoArtigo[]).map((t) => <option key={t} value={t}>{NOME_TIPO_ARTIGO[t]}</option>)}
          </select>
        </div>
        <div className="sm:col-span-2">
          <label htmlFor="nomeArt" className="rotulo">Nome</label>
          <input id="nomeArt" className="campo" maxLength={150} value={nome} onChange={(e) => setNome(e.target.value)} />
        </div>
        <div>
          <label htmlFor="unArt" className="rotulo">Unidade de stock</label>
          <select id="unArt" className="campo" value={unidadeId} onChange={(e) => setUnidadeId(Number(e.target.value))}>
            {unidades.map((u) => <option key={u.id} value={u.id}>{u.nome} ({u.codigo})</option>)}
          </select>
          {artigo && <p className="mt-1 text-xs text-slate-500">Não pode ser alterada depois de haver movimentos.</p>}
        </div>
        <div>
          <label htmlFor="minArt" className="rotulo">Stock mínimo (alerta)</label>
          <input id="minArt" type="number" min={0} step="0.001" className="campo" value={minimo} onChange={(e) => setMinimo(e.target.value)} />
        </div>
        <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
          <input type="checkbox" className="h-4 w-4" checked={negativo} disabled={!tem('stock.ajustar')} onChange={(e) => setNegativo(e.target.checked)} />
          Permitir stock negativo (excepção; exige permissão de ajustes)
        </label>
      </div>
    </Modal>
  )
}

function AjusteModal({ artigo, armazens, aoFechar }: { artigo: Artigo; armazens: Armazem[]; aoFechar: (recarregar?: boolean) => void }) {
  const avisos = useAvisos()
  const activos = armazens.filter((a) => a.activo)
  const [tipo, setTipo] = useState<'Ajuste' | 'Perda'>('Perda')
  const [armazemId, setArmazemId] = useState(activos.find((a) => a.principal)?.id ?? activos[0]?.id ?? 0)
  const [quantidade, setQuantidade] = useState('')
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const q = numero(quantidade)
  const valido = Number.isFinite(q) && q !== 0 && (tipo === 'Ajuste' || q > 0) && motivo.trim().length >= 3

  const guardar = async () => {
    setErro(null)
    setAGuardar(true)
    try {
      await api.post('/api/stock/ajustes', { artigoId: artigo.id, armazemId, tipo, quantidade: q, motivo: motivo.trim() })
      avisos.sucesso(tipo === 'Perda' ? 'Perda registada.' : 'Ajuste registado.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={`Ajustar stock: ${artigo.nome}`} aoFechar={() => aoFechar()}
      rodape={<Rodape aoFechar={() => aoFechar()} aoGuardar={() => void guardar()} aGuardar={aGuardar} texto="Registar" desactivado={!valido} />}>
      <div className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div className="flex gap-2">
          {(['Perda', 'Ajuste'] as const).map((t) => (
            <button key={t} type="button" className={`${tipo === t ? 'btn-primario' : 'btn-secundario'} flex-1`} onClick={() => setTipo(t)}>
              {t === 'Perda' ? 'Perda / quebra' : 'Ajuste (+/−)'}
            </button>
          ))}
        </div>
        <p className="text-sm text-slate-600">
          Saldo actual: <strong>{n3.format(artigo.saldo)} {artigo.unidade}</strong>.{' '}
          {tipo === 'Perda' ? 'Indique a quantidade perdida (positiva).' : 'Use valores negativos para retirar e positivos para acrescentar. Para contagens completas use um inventário.'}
        </p>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <label htmlFor="armAj" className="rotulo">Armazém</label>
            <select id="armAj" className="campo" value={armazemId} onChange={(e) => setArmazemId(Number(e.target.value))}>
              {activos.map((a) => <option key={a.id} value={a.id}>{a.nome}</option>)}
            </select>
          </div>
          <div>
            <label htmlFor="qtdAj" className="rotulo">Quantidade ({artigo.unidade})</label>
            <input id="qtdAj" type="number" step="0.001" className="campo" value={quantidade} onChange={(e) => setQuantidade(e.target.value)} />
          </div>
        </div>
        <div>
          <label htmlFor="motAj" className="rotulo">Motivo (obrigatório)</label>
          <input id="motAj" className="campo" maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Ex.: produto estragado, garrafa partida" />
        </div>
      </div>
    </Modal>
  )
}

function TransferirModal({ artigo, armazens, aoFechar }: { artigo: Artigo; armazens: Armazem[]; aoFechar: (recarregar?: boolean) => void }) {
  const avisos = useAvisos()
  const activos = armazens.filter((a) => a.activo)
  const [origem, setOrigem] = useState(activos[0]?.id ?? 0)
  const [destino, setDestino] = useState(activos[1]?.id ?? 0)
  const [quantidade, setQuantidade] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const q = numero(quantidade)

  const guardar = async () => {
    setErro(null)
    setAGuardar(true)
    try {
      await api.post('/api/stock/transferencias', { artigoId: artigo.id, armazemOrigemId: origem, armazemDestinoId: destino, quantidade: q, motivo: null })
      avisos.sucesso('Transferência registada.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={`Transferir ${artigo.nome}`} aoFechar={() => aoFechar()}
      rodape={<Rodape aoFechar={() => aoFechar()} aoGuardar={() => void guardar()} aGuardar={aGuardar} texto="Transferir" desactivado={!(q > 0) || origem === destino} />}>
      <div className="grid gap-4 sm:grid-cols-3">
        {erro && <div className="sm:col-span-3"><Alerta tipo="erro">{erro}</Alerta></div>}
        <div>
          <label htmlFor="orTr" className="rotulo">De</label>
          <select id="orTr" className="campo" value={origem} onChange={(e) => setOrigem(Number(e.target.value))}>
            {activos.map((a) => <option key={a.id} value={a.id}>{a.nome} ({n3.format(artigo.porArmazem.find((s) => s.armazemId === a.id)?.quantidade ?? 0)})</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="deTr" className="rotulo">Para</label>
          <select id="deTr" className="campo" value={destino} onChange={(e) => setDestino(Number(e.target.value))}>
            {activos.map((a) => <option key={a.id} value={a.id}>{a.nome}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="qTr" className="rotulo">Quantidade ({artigo.unidade})</label>
          <input id="qTr" type="number" min={0} step="0.001" className="campo" value={quantidade} onChange={(e) => setQuantidade(e.target.value)} />
        </div>
      </div>
    </Modal>
  )
}

/** Histórico de movimentos (de um artigo, ou de todos). Os movimentos são imutáveis. */
export function ListaMovimentos({ artigoId }: { artigoId?: number }) {
  const [pagina, setPagina] = useState(1)
  const lista = useDados(() => api.get<Pagina<MovimentoStock>>('/api/stock/movimentos' + query({ artigoId, pagina, tamanho: 50 })), [artigoId, pagina])
  if (lista.erro) return <Alerta tipo="erro">{lista.erro}</Alerta>
  if (!lista.dados) return <Carregando />
  if (lista.dados.itens.length === 0) return <Vazio texto="Sem movimentos." />
  return (
    <div>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-slate-600">
            <tr>
              <th scope="col" className="px-3 py-2">Data</th>
              {!artigoId && <th scope="col" className="px-3 py-2">Artigo</th>}
              <th scope="col" className="px-3 py-2">Tipo</th>
              <th scope="col" className="px-3 py-2">Armazém</th>
              <th scope="col" className="px-3 py-2 text-right">Quantidade</th>
              <th scope="col" className="px-3 py-2 text-right">Custo unit.</th>
              <th scope="col" className="px-3 py-2">Motivo / origem</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 tabular-nums">
            {lista.dados.itens.map((m) => (
              <tr key={m.id}>
                <td className="whitespace-nowrap px-3 py-2">{formatarDataHora(m.dataUtc)}</td>
                {!artigoId && <td className="px-3 py-2">{m.artigo}</td>}
                <td className="px-3 py-2">{NOME_MOVIMENTO[m.tipo]}</td>
                <td className="px-3 py-2">{m.armazem}</td>
                <td className={`px-3 py-2 text-right font-medium ${m.quantidade < 0 ? 'text-red-700' : 'text-green-700'}`}>
                  {m.quantidade > 0 ? '+' : ''}{n3.format(m.quantidade)} {m.unidade}
                </td>
                <td className="px-3 py-2 text-right">{formatarMoeda(m.custoUnitario)}</td>
                <td className="px-3 py-2 text-slate-600">{m.motivo}{m.utilizador && <span className="text-xs text-slate-500"> · {m.utilizador}</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Paginacao pagina={pagina} tamanho={50} total={lista.dados.total} aoMudar={setPagina} />
    </div>
  )
}
