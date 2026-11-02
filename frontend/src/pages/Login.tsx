import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Loader2, LogIn } from 'lucide-react'
import { ErroApi } from '../api/cliente'
import { Alerta, ErroCampo } from '../components/ui'
import { DESCRICAO_LIGACAO, useLigacao } from '../contexts/LigacaoContext'
import { useSessao } from '../contexts/SessaoContext'

const esquema = z.object({
  nomeUtilizador: z.string().trim().min(1, 'Indique o nome de utilizador.'),
  palavraPasse: z.string().min(1, 'Indique a palavra-passe.'),
})
type Dados = z.infer<typeof esquema>

export function Login() {
  const { utilizador, entrar } = useSessao()
  const { estado, podeGravar } = useLigacao()
  const navegar = useNavigate()
  const local = useLocation()
  const [erro, setErro] = useState<string | null>(null)
  const { register, handleSubmit, formState } = useForm<Dados>({ resolver: zodResolver(esquema) })

  if (utilizador) return <Navigate to="/" replace />

  const submeter = handleSubmit(async (dados) => {
    setErro(null)
    try {
      const sessao = await entrar(dados.nomeUtilizador, dados.palavraPasse)
      const destino = (local.state as { de?: string } | null)?.de ?? '/'
      navegar(sessao.deveAlterarPalavraPasse ? '/alterar-palavra-passe' : destino, { replace: true })
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível iniciar sessão.')
    }
  })

  return (
    <div className="flex min-h-full items-center justify-center bg-gradient-to-br from-primaria-800 to-primaria-600 p-4">
      <div className="cartao w-full max-w-sm p-8">
        <div className="mb-6 flex items-center gap-3">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-primaria-600 text-lg font-bold text-white">LG</div>
          <div>
            <h1 className="text-xl font-semibold">LAMGest</h1>
            <p className="text-sm text-slate-500">Restaurant ERP</p>
          </div>
        </div>

        <form onSubmit={submeter} noValidate className="space-y-4">
          {!podeGravar && estado !== 'a-verificar' && <Alerta tipo="erro">{DESCRICAO_LIGACAO[estado]}. Não é possível iniciar sessão.</Alerta>}
          {erro && <Alerta tipo="erro">{erro}</Alerta>}

          <div>
            <label htmlFor="nomeUtilizador" className="rotulo">Utilizador</label>
            <input
              id="nomeUtilizador"
              autoComplete="username"
              autoFocus
              className={`campo ${formState.errors.nomeUtilizador ? 'campo-erro' : ''}`}
              {...register('nomeUtilizador')}
            />
            <ErroCampo mensagem={formState.errors.nomeUtilizador?.message} />
          </div>
          <div>
            <label htmlFor="palavraPasse" className="rotulo">Palavra-passe</label>
            <input
              id="palavraPasse"
              type="password"
              autoComplete="current-password"
              className={`campo ${formState.errors.palavraPasse ? 'campo-erro' : ''}`}
              {...register('palavraPasse')}
            />
            <ErroCampo mensagem={formState.errors.palavraPasse?.message} />
          </div>
          <button type="submit" className="btn-primario w-full" disabled={formState.isSubmitting || !podeGravar}>
            {formState.isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <LogIn className="h-4 w-4" aria-hidden />}
            Entrar
          </button>
        </form>
        <p className="mt-6 text-center text-xs text-slate-500">Esqueceu a palavra-passe? Peça ao administrador para a repor.</p>
      </div>
    </div>
  )
}
