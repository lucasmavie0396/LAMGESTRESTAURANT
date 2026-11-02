import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { ShieldAlert } from 'lucide-react'
import { useSessao } from '../contexts/SessaoContext'
import { Carregando } from './ui'

/**
 * Protege rotas no frontend. É apenas conveniência de navegação: a autorização real é feita pela API.
 * Enquanto o utilizador tiver de alterar a palavra-passe, é encaminhado para esse ecrã.
 */
export function RotaProtegida({ children, permissoes }: { children: ReactNode; permissoes?: string[] }) {
  const { utilizador, aCarregar, tem } = useSessao()
  const local = useLocation()

  if (aCarregar) return <Carregando texto="A verificar sessão…" />
  if (!utilizador) return <Navigate to="/login" replace state={{ de: local.pathname }} />
  if (utilizador.deveAlterarPalavraPasse && local.pathname !== '/alterar-palavra-passe')
    return <Navigate to="/alterar-palavra-passe" replace />
  if (permissoes && !permissoes.some(tem)) return <SemPermissao />
  return <>{children}</>
}

function SemPermissao() {
  return (
    <div className="cartao mx-auto mt-10 max-w-md p-8 text-center">
      <ShieldAlert className="mx-auto h-10 w-10 text-amber-500" aria-hidden />
      <h1 className="mt-3 text-lg font-semibold">Sem permissão</h1>
      <p className="mt-1 text-sm text-slate-600">O seu perfil não tem acesso a esta área. Contacte o administrador se precisar de acesso.</p>
    </div>
  )
}
