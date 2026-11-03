import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useSessao } from './SessaoContext'

export type EstadoTempoReal = 'desligado' | 'a-ligar' | 'ligado' | 'a-religar'

/** Eventos enviados pelo servidor (ver NotificadorSignalR). "religado" é local: após reconexão, recarregar tudo. */
export interface EventosTempoReal {
  pedidoAlterado: { pedidoId: number; afectaCozinha: boolean }
  mesasAlteradas: Record<string, never>
  itensProntos: { pedidoId: number; pedidoNumero: string; mesa: string | null; produtos: string[] }
  /** Impressora em modo PDF: documento pronto para este utilizador. */
  documentoPdf: { url: string; titulo: string; impressora: string }
  religado: Record<string, never>
}
type NomeEvento = keyof EventosTempoReal
type Ouvinte<E extends NomeEvento> = (dados: EventosTempoReal[E]) => void

interface ValorTempoReal {
  estado: EstadoTempoReal
  subscrever: <E extends NomeEvento>(evento: E, ouvinte: Ouvinte<E>) => () => void
}

const TempoRealContext = createContext<ValorTempoReal | null>(null)
const EVENTOS_SERVIDOR: NomeEvento[] = ['pedidoAlterado', 'mesasAlteradas', 'itensProntos', 'documentoPdf']

/**
 * Uma ligação SignalR por separador, aberta só com sessão iniciada. Reconecta automaticamente;
 * se a reconexão automática desistir, volta a tentar a cada 5 s. Os eventos são apenas avisos
 * para recarregar dados pela API (nunca se confia em dados vindos pelo canal).
 */
export function TempoRealProvider({ children }: { children: ReactNode }) {
  const { utilizador } = useSessao()
  const [estado, setEstado] = useState<EstadoTempoReal>('desligado')
  const ouvintes = useRef(new Map<NomeEvento, Set<(d: unknown) => void>>())

  const emitir = (evento: NomeEvento, dados: unknown) => ouvintes.current.get(evento)?.forEach((o) => o(dados))

  useEffect(() => {
    if (!utilizador || utilizador.deveAlterarPalavraPasse) return
    let terminado = false
    let temporizador: number | undefined
    const ligacao: HubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/operacoes')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build()

    for (const e of EVENTOS_SERVIDOR) ligacao.on(e, (d: unknown) => emitir(e, d))
    ligacao.onreconnecting(() => setEstado('a-religar'))
    ligacao.onreconnected(() => {
      setEstado('ligado')
      emitir('religado', {})
    })
    ligacao.onclose(() => {
      if (terminado) return
      setEstado('desligado')
      temporizador = window.setTimeout(() => void iniciar(), 5000)
    })

    const iniciar = async () => {
      if (terminado || ligacao.state !== HubConnectionState.Disconnected) return
      setEstado('a-ligar')
      try {
        await ligacao.start()
        setEstado('ligado')
        emitir('religado', {})
      } catch {
        setEstado('desligado')
        if (!terminado) temporizador = window.setTimeout(() => void iniciar(), 5000)
      }
    }
    void iniciar()

    return () => {
      terminado = true
      window.clearTimeout(temporizador)
      void ligacao.stop()
      setEstado('desligado')
    }
  }, [utilizador])

  const valor = useMemo<ValorTempoReal>(
    () => ({
      estado,
      subscrever: (evento, ouvinte) => {
        const conjunto = ouvintes.current.get(evento) ?? new Set()
        conjunto.add(ouvinte as (d: unknown) => void)
        ouvintes.current.set(evento, conjunto)
        return () => conjunto.delete(ouvinte as (d: unknown) => void)
      },
    }),
    [estado],
  )

  return <TempoRealContext.Provider value={valor}>{children}</TempoRealContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useTempoReal() {
  const ctx = useContext(TempoRealContext)
  if (!ctx) throw new Error('useTempoReal fora de TempoRealProvider')
  return ctx
}

/** Subscreve um evento enquanto o componente está montado (o ouvinte mais recente é sempre usado). */
// eslint-disable-next-line react-refresh/only-export-components
export function useEvento<E extends NomeEvento>(evento: E, ouvinte: Ouvinte<E>) {
  const { subscrever } = useTempoReal()
  const ref = useRef(ouvinte)
  ref.current = ouvinte
  useEffect(() => subscrever(evento, (d) => ref.current(d)), [evento, subscrever])
}
