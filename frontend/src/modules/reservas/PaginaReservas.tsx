import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronLeft, ChevronRight, Clock, Loader2, Pencil, Phone, Plus, Users } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { EstadoReserva, Mesa, Reserva } from '../../types/api'
import { dataCurta, hojeIso, horaLocal, localParaUtc, somarDias, utcParaLocal } from '../../utils/datas'
import { DialogoMotivo } from '../pos/Dialogos'

const COR: Record<EstadoReserva, 'amarelo' | 'azul' | 'verde' | 'cinza' | 'vermelho'> = {
  Pendente: 'amarelo', Confirmada: 'azul', Chegada: 'verde', Concluida: 'cinza', Cancelada: 'vermelho',
}
const NOME: Record<EstadoReserva, string> = { Pendente: 'Pendente', Confirmada: 'Confirmada', Chegada: 'Chegou', Concluida: 'Concluída', Cancelada: 'Cancelada' }
const ACCOES: Partial<Record<EstadoReserva, { estado: EstadoReserva; texto: string }[]>> = {
  Pendente: [{ estado: 'Confirmada', texto: 'Confirmar' }, { estado: 'Chegada', texto: 'Chegou' }],
  Confirmada: [{ estado: 'Chegada', texto: 'Chegou' }],
  Chegada: [{ estado: 'Concluida', texto: 'Concluir' }],
}

export function PaginaReservas() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const navegar = useNavigate()
  const [dia, setDia] = useState(hojeIso)
  const [mostrarCanceladas, setMostrarCanceladas] = useState(false)
  const [editar, setEditar] = useState<Reserva | 'nova' | null>(null)
  const [cancelar, setCancelar] = useState<Reserva | null>(null)
  const lista = useDados(() => api.get<Reserva[]>('/api/reservas' + query({ desde: dia, ate: dia })), [dia])
  const mesas = useDados(() => api.get<Mesa[]>('/api/mesas'), [])

  const mudarEstado = async (r: Reserva, estado: EstadoReserva, motivo: string | null = null) => {
    try {
      await api.post(`/api/reservas/${r.id}/estado`, { estado, motivo, rowVersion: r.rowVersion })
      avisos.sucesso(`Reserva de ${r.nomeCliente}: ${NOME[estado].toLowerCase()}.`)
      await lista.recarregar()
      if (estado === 'Chegada' && r.mesaId && tem('pos.usar')) navegar(`/pos?mesa=${r.mesaId}`)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const reservas = (lista.dados ?? []).filter((r) => mostrarCanceladas || r.estado !== 'Cancelada')
  const pessoas = reservas.filter((r) => r.estado !== 'Cancelada').reduce((s, r) => s + r.numeroPessoas, 0)

  return (
    <div className="mx-auto max-w-5xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Reservas</h1>
        <button type="button" className="btn-primario" onClick={() => setEditar('nova')} disabled={!podeGravar}><Plus className="h-4 w-4" aria-hidden /> Nova reserva</button>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" className="btn-icone" aria-label="Dia anterior" onClick={() => setDia((d) => somarDias(d, -1))}><ChevronLeft className="h-5 w-5" /></button>
        <input type="date" className="campo w-44" value={dia} onChange={(e) => e.target.value && setDia(e.target.value)} aria-label="Dia" />
        <button type="button" className="btn-icone" aria-label="Dia seguinte" onClick={() => setDia((d) => somarDias(d, 1))}><ChevronRight className="h-5 w-5" /></button>
        {dia !== hojeIso() && <button type="button" className="btn-secundario min-h-9 px-3" onClick={() => setDia(hojeIso())}>Hoje</button>}
        <span className="text-sm text-slate-600">{reservas.length} reservas · {pessoas} pessoas</span>
        <label className="ml-auto flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4" checked={mostrarCanceladas} onChange={(e) => setMostrarCanceladas(e.target.checked)} /> Mostrar canceladas</label>
      </div>
      {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados && reservas.length === 0 && <Vazio texto={`Sem reservas para ${dataCurta(dia)}.`} />}
      <ul className="space-y-2">
        {reservas.map((r) => (
          <li key={r.id} className={`cartao flex flex-wrap items-center gap-4 p-4 ${r.estado === 'Cancelada' ? 'opacity-60' : ''}`}>
            <div className="w-20 text-center">
              <p className="text-xl font-bold tabular-nums">{horaLocal(r.inicioUtc)}</p>
              <p className="flex items-center justify-center gap-1 text-xs text-slate-500"><Clock className="h-3 w-3" aria-hidden />{r.duracaoMinutos} min</p>
            </div>
            <div className="min-w-48 flex-1">
              <p className="font-semibold">{r.nomeCliente}</p>
              <p className="flex flex-wrap items-center gap-3 text-sm text-slate-600">
                <span className="flex items-center gap-1"><Users className="h-3.5 w-3.5" aria-hidden />{r.numeroPessoas}</span>
                {r.telefone && <span className="flex items-center gap-1"><Phone className="h-3.5 w-3.5" aria-hidden />{r.telefone}</span>}
                <span>{r.mesaNome ? `Mesa ${r.mesaNome}` : 'Sem mesa atribuída'}</span>
              </p>
              {r.observacoes && <p className="text-sm italic text-slate-500">{r.observacoes}</p>}
              {r.motivoCancelamento && <p className="text-sm text-red-700">Cancelada: {r.motivoCancelamento}</p>}
            </div>
            <Etiqueta cor={COR[r.estado]}>{NOME[r.estado]}</Etiqueta>
            <div className="flex flex-wrap gap-2">
              {ACCOES[r.estado]?.map((a) => (
                <button key={a.estado} type="button" className="btn-primario min-h-10" disabled={!podeGravar} onClick={() => void mudarEstado(r, a.estado)}>{a.texto}</button>
              ))}
              {(r.estado === 'Pendente' || r.estado === 'Confirmada') && (
                <>
                  <button type="button" className="btn-icone" aria-label={`Editar reserva de ${r.nomeCliente}`} disabled={!podeGravar} onClick={() => setEditar(r)}><Pencil className="h-4 w-4" /></button>
                  <button type="button" className="btn-secundario min-h-10 text-red-700" disabled={!podeGravar} onClick={() => setCancelar(r)}>Cancelar</button>
                </>
              )}
            </div>
          </li>
        ))}
      </ul>
      {editar && mesas.dados && (
        <FormReserva reserva={editar === 'nova' ? undefined : editar} diaInicial={dia} mesas={mesas.dados}
          aoFechar={(ok) => { setEditar(null); if (ok) void lista.recarregar() }} />
      )}
      {cancelar && (
        <DialogoMotivo titulo={`Cancelar reserva de ${cancelar.nomeCliente}`} texto="Cancelar reserva" descricao="A mesa fica livre para outras reservas nesse horário."
          aoFechar={() => setCancelar(null)} aoConfirmar={async (motivo) => { await mudarEstado(cancelar, 'Cancelada', motivo); setCancelar(null) }} />
      )}
    </div>
  )
}

function FormReserva({ reserva, diaInicial, mesas, aoFechar }: { reserva?: Reserva; diaInicial: string; mesas: Mesa[]; aoFechar: (ok?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const inicial = reserva ? utcParaLocal(reserva.inicioUtc) : { data: diaInicial, hora: '19:00' }
  const [nome, setNome] = useState(reserva?.nomeCliente ?? '')
  const [telefone, setTelefone] = useState(reserva?.telefone ?? '')
  const [data, setData] = useState(inicial.data)
  const [hora, setHora] = useState(inicial.hora)
  const [duracao, setDuracao] = useState(reserva?.duracaoMinutos ?? 120)
  const [pessoas, setPessoas] = useState(reserva?.numeroPessoas ?? 2)
  const [mesaId, setMesaId] = useState<number | ''>(reserva?.mesaId ?? '')
  const [observacoes, setObservacoes] = useState(reserva?.observacoes ?? '')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const mesasAdequadas = mesas.filter((m) => m.activo && m.estado !== 'Indisponivel' && m.capacidade >= pessoas)

  const guardar = async () => {
    if (!nome.trim()) return setErro('Indique o nome do cliente.')
    setErro(null)
    setAGuardar(true)
    const corpo = {
      clienteId: reserva?.clienteId ?? null, nomeCliente: nome.trim(), telefone: telefone.trim() || null, inicioUtc: localParaUtc(data, hora),
      duracaoMinutos: duracao, numeroPessoas: pessoas, mesaId: mesaId === '' ? null : mesaId, observacoes: observacoes.trim() || null, rowVersion: reserva?.rowVersion ?? null,
    }
    try {
      if (reserva) await api.put(`/api/reservas/${reserva.id}`, corpo)
      else await api.post('/api/reservas', corpo)
      avisos.sucesso(reserva ? 'Reserva actualizada.' : 'Reserva registada.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={reserva ? 'Editar reserva' : 'Nova reserva'} aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="grid gap-4 sm:grid-cols-2">
        {erro && <div className="sm:col-span-2"><Alerta tipo="erro">{erro}</Alerta></div>}
        <div><label htmlFor="rNome" className="rotulo">Nome do cliente</label><input id="rNome" className="campo" maxLength={200} value={nome} onChange={(e) => setNome(e.target.value)} /></div>
        <div><label htmlFor="rTel" className="rotulo">Telefone</label><input id="rTel" type="tel" className="campo" maxLength={20} value={telefone} onChange={(e) => setTelefone(e.target.value)} /></div>
        <div><label htmlFor="rData" className="rotulo">Data</label><input id="rData" type="date" className="campo" min={hojeIso()} value={data} onChange={(e) => setData(e.target.value)} /></div>
        <div><label htmlFor="rHora" className="rotulo">Hora</label><input id="rHora" type="time" className="campo" value={hora} onChange={(e) => setHora(e.target.value)} /></div>
        <div><label htmlFor="rPessoas" className="rotulo">Pessoas</label><input id="rPessoas" type="number" min={1} max={200} className="campo" value={pessoas} onChange={(e) => setPessoas(Number(e.target.value) || 1)} /></div>
        <div>
          <label htmlFor="rDur" className="rotulo">Duração</label>
          <select id="rDur" className="campo" value={duracao} onChange={(e) => setDuracao(Number(e.target.value))}>
            {[60, 90, 120, 150, 180, 240].map((m) => <option key={m} value={m}>{m / 60 >= 1 ? `${Math.floor(m / 60)} h${m % 60 ? ` ${m % 60} min` : ''}` : `${m} min`}</option>)}
          </select>
        </div>
        <div className="sm:col-span-2">
          <label htmlFor="rMesa" className="rotulo">Mesa (opcional)</label>
          <select id="rMesa" className="campo" value={mesaId} onChange={(e) => setMesaId(e.target.value ? Number(e.target.value) : '')}>
            <option value="">Sem mesa atribuída</option>
            {mesasAdequadas.map((m) => <option key={m.id} value={m.id}>Mesa {m.nome} · {m.zonaNome} · {m.capacidade} lugares</option>)}
          </select>
          <p className="mt-1 text-xs text-slate-500">Só aparecem mesas com lugares suficientes. Conflitos de horário são verificados ao guardar.</p>
        </div>
        <div className="sm:col-span-2"><label htmlFor="rObs" className="rotulo">Observações</label><textarea id="rObs" rows={2} className="campo py-2" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} /></div>
      </div>
    </Modal>
  )
}
