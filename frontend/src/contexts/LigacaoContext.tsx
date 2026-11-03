import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { EstadoLicencaDto, EstadoSaude } from '../types/api'

export type EstadoLigacao = 'a-verificar' | 'ligado' | 'sem-servidor' | 'sem-base-dados' | 'migracoes-pendentes'

interface ValorLigacao {
  estado: EstadoLigacao
  /** Só se pode gravar com servidor e base de dados disponíveis (não há modo offline) e licença que não bloqueie. */
  podeGravar: boolean
  ultimaVerificacao: Date | null
  verificarAgora: () => void
  /** Estado da licença (null enquanto não se sabe). */
  licenca: EstadoLicencaDto | null
  actualizarLicenca: () => void
}

const INTERVALO_MS = 10_000
const INTERVALO_FALHA_MS = 3_000
/** O servidor recalcula a licença de 5 em 5 minutos; aqui basta de minuto a minuto. */
const INTERVALO_LICENCA_MS = 60_000

const LigacaoContext = createContext<ValorLigacao | null>(null)

/**
 * Monitoriza a ligação ao servidor através de /api/saude.
 * Com ligação estável verifica a cada 10 s; após falha, a cada 3 s até recuperar.
 * (Quando o SignalR for adicionado, o estado da ligação em tempo real complementa esta verificação.)
 */
export function LigacaoProvider({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState<EstadoLigacao>('a-verificar')
  const [ultimaVerificacao, setUltimaVerificacao] = useState<Date | null>(null)
  const temporizador = useRef<number | undefined>(undefined)

  const verificar = useCallback(async () => {
    window.clearTimeout(temporizador.current)
    let novo: EstadoLigacao
    try {
      const resposta = await fetch('/api/saude', { cache: 'no-store', signal: AbortSignal.timeout(5000) })
      const saude = (await resposta.json()) as EstadoSaude
      novo = !saude.baseDados ? 'sem-base-dados' : saude.migracoesPendentes ? 'migracoes-pendentes' : 'ligado'
    } catch {
      novo = 'sem-servidor'
    }
    setEstado(novo)
    setUltimaVerificacao(new Date())
    temporizador.current = window.setTimeout(() => void verificar(), novo === 'ligado' ? INTERVALO_MS : INTERVALO_FALHA_MS)
  }, [])

  useEffect(() => {
    void verificar()
    const online = () => void verificar()
    window.addEventListener('online', online)
    window.addEventListener('offline', online)
    return () => {
      window.clearTimeout(temporizador.current)
      window.removeEventListener('online', online)
      window.removeEventListener('offline', online)
    }
  }, [verificar])

  // Licença: verificada ao abrir e de minuto a minuto (o pedido é público, também no ecrã de entrada).
  const [licenca, setLicenca] = useState<EstadoLicencaDto | null>(null)
  const actualizarLicenca = useCallback(async () => {
    try {
      const r = await fetch('/api/licenca/estado', { cache: 'no-store', credentials: 'same-origin', signal: AbortSignal.timeout(5000) })
      if (r.ok) setLicenca((await r.json()) as EstadoLicencaDto)
    } catch {
      // Sem servidor: o indicador de ligação já o mostra; mantém-se o último estado conhecido.
    }
  }, [])
  useEffect(() => {
    void actualizarLicenca()
    const id = window.setInterval(() => void actualizarLicenca(), INTERVALO_LICENCA_MS)
    return () => window.clearInterval(id)
  }, [actualizarLicenca])

  const valor = useMemo<ValorLigacao>(
    () => ({
      estado, podeGravar: estado === 'ligado' && !licenca?.bloqueada, ultimaVerificacao, verificarAgora: () => void verificar(),
      licenca, actualizarLicenca: () => void actualizarLicenca(),
    }),
    [estado, ultimaVerificacao, verificar, licenca, actualizarLicenca],
  )
  return <LigacaoContext.Provider value={valor}>{children}</LigacaoContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useLigacao() {
  const ctx = useContext(LigacaoContext)
  if (!ctx) throw new Error('useLigacao fora de LigacaoProvider')
  return ctx
}

// eslint-disable-next-line react-refresh/only-export-components
export const DESCRICAO_LIGACAO: Record<EstadoLigacao, string> = {
  'a-verificar': 'A verificar ligação…',
  ligado: 'Ligado ao servidor',
  'sem-servidor': 'Sem ligação ao servidor',
  'sem-base-dados': 'Base de dados indisponível',
  'migracoes-pendentes': 'Base de dados desactualizada',
}
