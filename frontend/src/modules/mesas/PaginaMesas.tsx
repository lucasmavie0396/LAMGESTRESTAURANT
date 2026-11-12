import { useEffect, useState } from 'react'
import { Clock, Loader2, Lock, Pencil, Plus, Power, Settings2, UserRound, Users } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useEvento } from '../../contexts/TempoRealContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import { useNavigate } from 'react-router-dom'
import type { EstadoMesa, Mesa, Zona } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { P } from '../../utils/permissoes'
import { avisoMesaDeOutro, mesaDeOutro } from '../../utils/mesas'

const ESTILO_ESTADO: Record<EstadoMesa, { rotulo: string; cartao: string; ponto: string }> = {
  Livre: { rotulo: 'Livre', cartao: 'border-green-300 bg-green-50', ponto: 'bg-green-500' },
  Ocupada: { rotulo: 'Ocupada', cartao: 'border-red-300 bg-red-50', ponto: 'bg-red-500' },
  Reservada: { rotulo: 'Reservada', cartao: 'border-amber-300 bg-amber-50', ponto: 'bg-amber-500' },
  Indisponivel: { rotulo: 'Indisponível', cartao: 'border-slate-300 bg-slate-100', ponto: 'bg-slate-400' },
}

function duracao(desdeUtc: string, agora: number) {
  const minutos = Math.max(0, Math.floor((agora - new Date(desdeUtc).getTime()) / 60000))
  return minutos < 60 ? `${minutos} min` : `${Math.floor(minutos / 60)} h ${minutos % 60} min`
}

export function PaginaMesas() {
  const { tem } = useSessao()
  const podeGerir = tem(P.mesasGerir)
  const [modoGestao, setModoGestao] = useState(false)
  const [zonaFiltro, setZonaFiltro] = useState<number | 'todas'>('todas')
  const [agora, setAgora] = useState(() => Date.now())

  const zonas = useDados(() => api.get<Zona[]>(`/api/zonas${modoGestao ? '?incluirInactivas=true' : ''}`), [modoGestao])
  const mesas = useDados(() => api.get<Mesa[]>(`/api/mesas${modoGestao ? '?incluirInactivas=true' : ''}`), [modoGestao])

  // Estados em tempo real (SignalR); o relógio só actualiza as durações apresentadas.
  useEvento('mesasAlteradas', () => void mesas.recarregar())
  useEvento('religado', () => void mesas.recarregar())
  useEffect(() => {
    const id = window.setInterval(() => setAgora(Date.now()), 30_000)
    return () => window.clearInterval(id)
  }, [])

  const zonasVisiveis = (zonas.dados ?? []).filter((z) => zonaFiltro === 'todas' || z.id === zonaFiltro)
  const contagem = (estado: EstadoMesa) => mesas.dados?.filter((m) => m.activo && m.estado === estado).length ?? 0

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Mesas</h1>
        {podeGerir && (
          <button type="button" className={modoGestao ? 'btn-primario' : 'btn-secundario'} onClick={() => setModoGestao((m) => !m)}>
            <Settings2 className="h-4 w-4" aria-hidden /> {modoGestao ? 'Terminar gestão' : 'Gerir mesas e zonas'}
          </button>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={() => setZonaFiltro('todas')}
          className={`min-h-11 rounded-full px-4 text-sm font-medium ${zonaFiltro === 'todas' ? 'bg-primaria-600 text-white' : 'bg-white text-slate-700 ring-1 ring-slate-300'}`}>
          Todas
        </button>
        {zonas.dados?.filter((z) => z.activo).map((z) => (
          <button key={z.id} type="button" onClick={() => setZonaFiltro(z.id)}
            className={`min-h-11 rounded-full px-4 text-sm font-medium ${zonaFiltro === z.id ? 'bg-primaria-600 text-white' : 'bg-white text-slate-700 ring-1 ring-slate-300'}`}>
            {z.nome}
          </button>
        ))}
        <div className="ml-auto flex flex-wrap gap-3 text-sm text-slate-600">
          {(Object.keys(ESTILO_ESTADO) as EstadoMesa[]).map((e) => (
            <span key={e} className="flex items-center gap-1.5">
              <span className={`h-3 w-3 rounded-full ${ESTILO_ESTADO[e].ponto}`} aria-hidden /> {ESTILO_ESTADO[e].rotulo} ({contagem(e)})
            </span>
          ))}
        </div>
      </div>

      {(zonas.erro || mesas.erro) && <Alerta tipo="erro">{zonas.erro ?? mesas.erro}</Alerta>}
      {(!zonas.dados || !mesas.dados) && !zonas.erro && !mesas.erro && <Carregando />}

      {modoGestao && zonas.dados && mesas.dados && (
        <GestaoSala zonas={zonas.dados} aoAlterar={async () => { await zonas.recarregar(); await mesas.recarregar() }} />
      )}

      {zonas.dados && mesas.dados && zonas.dados.length === 0 && (
        <Vazio texto={podeGerir ? 'Ainda não há zonas. Use «Gerir mesas e zonas» para criar a primeira.' : 'Ainda não há mesas configuradas.'} />
      )}

      {zonas.dados && mesas.dados &&
        zonasVisiveis.map((z) => {
          const daZona = mesas.dados!.filter((m) => m.zonaId === z.id)
          if (!modoGestao && daZona.filter((m) => m.activo).length === 0) return null
          return (
            <section key={z.id} aria-labelledby={`zona-${z.id}`}>
              <h2 id={`zona-${z.id}`} className="mb-2 flex items-center gap-2 font-semibold text-slate-700">
                {z.nome} {!z.activo && <Etiqueta cor="cinza">Inactiva</Etiqueta>}
              </h2>
              {daZona.length === 0 ? (
                <p className="text-sm text-slate-500">Sem mesas nesta zona.</p>
              ) : (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
                  {daZona.map((m) => (
                    <CartaoMesa key={m.id} mesa={m} agora={agora} modoGestao={modoGestao} zonas={zonas.dados!} aoAlterar={mesas.recarregar} />
                  ))}
                </div>
              )}
            </section>
          )
        })}
    </div>
  )
}

function CartaoMesa({ mesa, agora, modoGestao, zonas, aoAlterar }: {
  mesa: Mesa
  agora: number
  modoGestao: boolean
  zonas: Zona[]
  aoAlterar: () => Promise<void>
}) {
  const { podeGravar } = useLigacao()
  const { tem, utilizador } = useSessao()
  const navegar = useNavigate()
  const avisos = useAvisos()
  const [editar, setEditar] = useState(false)
  const estilo = ESTILO_ESTADO[mesa.estado]
  // Mesa aberta por outro utilizador: sem pos.mesas-outros vê-se quem a abriu, mas não o conteúdo.
  const deOutro = mesaDeOutro(mesa, utilizador?.id, tem(P.posMesasOutros))
  // Fora do modo de gestão, tocar na mesa abre-a no POS (pedido existente ou novo).
  const abrivel = !modoGestao && mesa.activo && mesa.estado !== 'Indisponivel' && tem(P.posUsar)
  const abrir = () => (deOutro ? avisos.erro(avisoMesaDeOutro(mesa)) : navegar(`/pos?mesa=${mesa.id}`))

  const executar = async (url: string, corpo: unknown, sucesso: string) => {
    try {
      await api.post(url, corpo)
      avisos.sucesso(sucesso)
      await aoAlterar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div
      className={`flex min-h-32 flex-col rounded-xl border-2 p-3 ${estilo.cartao} ${mesa.activo ? '' : 'opacity-50'} ${abrivel ? 'cursor-pointer hover:shadow-md' : ''}`}
      role={abrivel ? 'button' : undefined}
      tabIndex={abrivel ? 0 : undefined}
      aria-label={abrivel ? `Abrir mesa ${mesa.nome} no POS` : undefined}
      onClick={abrivel ? abrir : undefined}
      onKeyDown={abrivel ? (e) => { if (e.key === 'Enter') abrir() } : undefined}
    >
      <div className="flex items-start justify-between">
        <span className="text-xl font-bold">{mesa.nome}</span>
        <span className="flex items-center gap-1 text-xs text-slate-600" title="Lugares">
          <Users className="h-3.5 w-3.5" aria-hidden /> {mesa.capacidade}
        </span>
      </div>
      <span className="mt-1 text-sm font-medium">{mesa.activo ? estilo.rotulo : 'Inactiva'}</span>
      {mesa.pedidosAbertos > 0 && !deOutro && <span className="text-sm font-semibold tabular-nums">{formatarMoeda(mesa.totalAberto)}</span>}
      {mesa.responsavel && (
        <span className="flex items-center gap-1 text-xs text-slate-700" data-testid="responsavel-mesa">
          {deOutro ? <Lock className="h-3.5 w-3.5" aria-hidden /> : <UserRound className="h-3.5 w-3.5" aria-hidden />}
          <span className="truncate">Aberta por {mesa.responsavel}</span>
        </span>
      )}
      {mesa.ocupadaDesdeUtc && (
        <span className="flex items-center gap-1 text-xs text-slate-600">
          <Clock className="h-3.5 w-3.5" aria-hidden /> {duracao(mesa.ocupadaDesdeUtc, agora)}
        </span>
      )}
      {modoGestao && (
        <div className="mt-auto flex flex-wrap gap-1 pt-2">
          <button type="button" className="btn-icone h-9 w-9 bg-white" title="Editar" aria-label={`Editar mesa ${mesa.nome}`} disabled={!podeGravar} onClick={() => setEditar(true)}>
            <Pencil className="h-4 w-4" />
          </button>
          {mesa.activo && (mesa.estado === 'Livre' || mesa.estado === 'Indisponivel') && (
            <button type="button" className="btn-secundario min-h-9 px-2 text-xs" disabled={!podeGravar}
              onClick={() => void executar(`/api/mesas/${mesa.id}/estado`, { estado: mesa.estado === 'Livre' ? 'Indisponivel' : 'Livre', rowVersion: mesa.rowVersion },
                mesa.estado === 'Livre' ? 'Mesa marcada como indisponível.' : 'Mesa disponível.')}>
              {mesa.estado === 'Livre' ? 'Indisponível' : 'Disponível'}
            </button>
          )}
          <button type="button" className="btn-icone h-9 w-9 bg-white" title={mesa.activo ? 'Desactivar' : 'Activar'} aria-label={`${mesa.activo ? 'Desactivar' : 'Activar'} mesa ${mesa.nome}`}
            disabled={!podeGravar}
            onClick={() => void executar(`/api/mesas/${mesa.id}/activo`, { activo: !mesa.activo, rowVersion: mesa.rowVersion }, mesa.activo ? 'Mesa desactivada.' : 'Mesa activada.')}>
            <Power className={`h-4 w-4 ${mesa.activo ? 'text-red-600' : 'text-green-700'}`} />
          </button>
        </div>
      )}
      {editar && (
        <EditorMesa mesa={mesa} zonas={zonas} aoFechar={() => setEditar(false)} aoGuardar={async () => { setEditar(false); await aoAlterar() }} />
      )}
    </div>
  )
}

function GestaoSala({ zonas, aoAlterar }: { zonas: Zona[]; aoAlterar: () => Promise<void> }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [novaMesa, setNovaMesa] = useState(false)
  const [nomeZona, setNomeZona] = useState('')
  const [aCriarZona, setACriarZona] = useState(false)

  const criarZona = async () => {
    setACriarZona(true)
    try {
      await api.post('/api/zonas', { nome: nomeZona.trim(), ordem: zonas.length + 1 })
      avisos.sucesso('Zona criada.')
      setNomeZona('')
      await aoAlterar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setACriarZona(false)
    }
  }

  const alternarZona = async (z: Zona) => {
    try {
      await api.post(`/api/zonas/${z.id}/activo`, { activo: !z.activo })
      avisos.sucesso(z.activo ? 'Zona desactivada.' : 'Zona activada.')
      await aoAlterar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="cartao space-y-4 p-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-60 flex-1">
          <label htmlFor="novaZona" className="rotulo">Nova zona</label>
          <input id="novaZona" className="campo" placeholder="Ex.: Sala principal, Esplanada, Bar" value={nomeZona} onChange={(e) => setNomeZona(e.target.value)} maxLength={200} />
        </div>
        <button type="button" className="btn-secundario" onClick={() => void criarZona()} disabled={!podeGravar || nomeZona.trim().length === 0 || aCriarZona}>
          {aCriarZona && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Criar zona
        </button>
        <button type="button" className="btn-primario" onClick={() => setNovaMesa(true)} disabled={!podeGravar || !zonas.some((z) => z.activo)}>
          <Plus className="h-4 w-4" aria-hidden /> Nova mesa
        </button>
      </div>
      {zonas.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {zonas.map((z) => (
            <span key={z.id} className="inline-flex items-center gap-2 rounded-full bg-slate-100 py-1 pl-3 pr-1 text-sm">
              {z.nome} ({z.numeroMesas})
              <button type="button" className="rounded-full px-2 py-1 text-xs font-medium text-slate-600 hover:bg-slate-200" disabled={!podeGravar} onClick={() => void alternarZona(z)}>
                {z.activo ? 'Desactivar' : 'Activar'}
              </button>
            </span>
          ))}
        </div>
      )}
      {novaMesa && <EditorMesa zonas={zonas} aoFechar={() => setNovaMesa(false)} aoGuardar={async () => { setNovaMesa(false); await aoAlterar() }} />}
    </div>
  )
}

function EditorMesa({ mesa, zonas, aoFechar, aoGuardar }: { mesa?: Mesa; zonas: Zona[]; aoFechar: () => void; aoGuardar: () => Promise<void> }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [nome, setNome] = useState(mesa?.nome ?? '')
  const [zonaId, setZonaId] = useState(mesa?.zonaId ?? zonas.find((z) => z.activo)?.id ?? 0)
  const [capacidade, setCapacidade] = useState(mesa?.capacidade ?? 4)
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const guardar = async () => {
    if (!nome.trim()) return setErro('Indique o nome/número da mesa.')
    if (capacidade < 1 || capacidade > 100) return setErro('A capacidade deve estar entre 1 e 100 lugares.')
    setErro(null)
    setAGuardar(true)
    const corpo = { nome: nome.trim(), zonaId, capacidade, rowVersion: mesa?.rowVersion ?? null }
    try {
      if (mesa) await api.put(`/api/mesas/${mesa.id}`, corpo)
      else await api.post('/api/mesas', corpo)
      avisos.sucesso(mesa ? 'Mesa actualizada.' : 'Mesa criada.')
      await aoGuardar()
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível guardar a mesa.')
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal
      titulo={mesa ? `Editar mesa ${mesa.nome}` : 'Nova mesa'}
      aoFechar={aoFechar}
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
        <div>
          <label htmlFor="nomeMesa" className="rotulo">Nome / número</label>
          <input id="nomeMesa" className="campo" value={nome} maxLength={30} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: 12, T3, Varanda 1" />
        </div>
        <div>
          <label htmlFor="zonaMesa" className="rotulo">Zona</label>
          <select id="zonaMesa" className="campo" value={zonaId} onChange={(e) => setZonaId(Number(e.target.value))}>
            {zonas.filter((z) => z.activo || z.id === mesa?.zonaId).map((z) => (
              <option key={z.id} value={z.id}>{z.nome}</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="capacidadeMesa" className="rotulo">Lugares</label>
          <input id="capacidadeMesa" type="number" min={1} max={100} className="campo" value={capacidade} onChange={(e) => setCapacidade(Number(e.target.value))} />
        </div>
      </div>
    </Modal>
  )
}
