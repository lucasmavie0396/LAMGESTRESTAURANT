import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, ErroApi, EVENTO_SESSAO_EXPIRADA } from '../api/cliente'
import type { SessaoUtilizador } from '../types/api'

interface ValorSessao {
  utilizador: SessaoUtilizador | null
  /** true enquanto se verifica se já existe uma sessão (arranque da aplicação). */
  aCarregar: boolean
  /** Erro ao verificar a sessão no arranque (ex.: servidor indisponível). */
  erroArranque: string | null
  entrar: (nomeUtilizador: string, palavraPasse: string) => Promise<SessaoUtilizador>
  /** Ecrã de entrada táctil: utilizador escolhido no botão e o seu código. */
  entrarComPin: (utilizadorId: number, pin: string) => Promise<SessaoUtilizador>
  sair: () => Promise<void>
  recarregar: () => Promise<void>
  tem: (permissao: string) => boolean
}

const SessaoContext = createContext<ValorSessao | null>(null)

export function SessaoProvider({ children }: { children: ReactNode }) {
  const [utilizador, setUtilizador] = useState<SessaoUtilizador | null>(null)
  const [aCarregar, setACarregar] = useState(true)
  const [erroArranque, setErroArranque] = useState<string | null>(null)

  const recarregar = useCallback(async () => {
    try {
      setUtilizador(await api.get<SessaoUtilizador>('/api/auth/me'))
      setErroArranque(null)
    } catch (e) {
      setUtilizador(null)
      if (e instanceof ErroApi && e.estado !== 401) setErroArranque(e.message)
    } finally {
      setACarregar(false)
    }
  }, [])

  useEffect(() => {
    void recarregar()
  }, [recarregar])

  useEffect(() => {
    const expirou = () => setUtilizador(null)
    window.addEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
    return () => window.removeEventListener(EVENTO_SESSAO_EXPIRADA, expirou)
  }, [])

  const entrar = useCallback(async (nomeUtilizador: string, palavraPasse: string) => {
    const sessao = await api.post<SessaoUtilizador>('/api/auth/login', { nomeUtilizador, palavraPasse })
    setUtilizador(sessao)
    setErroArranque(null)
    return sessao
  }, [])

  const entrarComPin = useCallback(async (utilizadorId: number, pin: string) => {
    const sessao = await api.post<SessaoUtilizador>('/api/auth/login-pin', { utilizadorId, pin })
    setUtilizador(sessao)
    setErroArranque(null)
    return sessao
  }, [])

  const sair = useCallback(async () => {
    try {
      await api.post('/api/auth/logout')
    } finally {
      setUtilizador(null)
    }
  }, [])

  const valor = useMemo<ValorSessao>(
    () => ({
      utilizador,
      aCarregar,
      erroArranque,
      entrar,
      entrarComPin,
      sair,
      recarregar,
      tem: (permissao) => utilizador?.permissoes.includes(permissao) ?? false,
    }),
    [utilizador, aCarregar, erroArranque, entrar, entrarComPin, sair, recarregar],
  )

  return <SessaoContext.Provider value={valor}>{children}</SessaoContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useSessao() {
  const ctx = useContext(SessaoContext)
  if (!ctx) throw new Error('useSessao fora de SessaoProvider')
  return ctx
}
