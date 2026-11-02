import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { EstadoSaude } from '../types/api'

export type EstadoLigacao = 'a-verificar' | 'ligado' | 'sem-servidor' | 'sem-base-dados' | 'migracoes-pendentes'

interface ValorLigacao {
  estado: EstadoLigacao
  /** Só se pode gravar com servidor e base de dados disponíveis (não há modo offline). */
  podeGravar: boolean
  ultimaVerificacao: Date | null
  verificarAgora: () => void
}

const INTERVALO_MS = 10_000
const INTERVALO_FALHA_MS = 3_000

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

  const valor = useMemo<ValorLigacao>(
    () => ({ estado, podeGravar: estado === 'ligado', ultimaVerificacao, verificarAgora: () => void verificar() }),
    [estado, ultimaVerificacao, verificar],
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
