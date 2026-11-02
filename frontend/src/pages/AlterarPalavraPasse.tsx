import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useNavigate } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { api, ErroApi } from '../api/cliente'
import { Alerta, ErroCampo } from '../components/ui'
import { useAvisos } from '../contexts/AvisosContext'
import { useLigacao } from '../contexts/LigacaoContext'
import { useSessao } from '../contexts/SessaoContext'
import { regraPalavraPasse } from '../utils/validacao'

const esquema = z
  .object({
    palavraPasseActual: z.string().min(1, 'Indique a palavra-passe actual.'),
    novaPalavraPasse: regraPalavraPasse,
    confirmacao: z.string(),
  })
  .refine((d) => d.novaPalavraPasse === d.confirmacao, { path: ['confirmacao'], message: 'As palavras-passe não coincidem.' })
  .refine((d) => d.novaPalavraPasse !== d.palavraPasseActual, { path: ['novaPalavraPasse'], message: 'A nova palavra-passe tem de ser diferente da actual.' })
type Dados = z.infer<typeof esquema>

export function AlterarPalavraPasse() {
  const { utilizador, recarregar, sair } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const navegar = useNavigate()
  const [erro, setErro] = useState<string | null>(null)
  const { register, handleSubmit, formState } = useForm<Dados>({ resolver: zodResolver(esquema) })
  const obrigatorio = utilizador?.deveAlterarPalavraPasse ?? false

  const submeter = handleSubmit(async (d) => {
    setErro(null)
    try {
      await api.post('/api/auth/alterar-palavra-passe', { palavraPasseActual: d.palavraPasseActual, novaPalavraPasse: d.novaPalavraPasse })
      await recarregar()
      avisos.sucesso('Palavra-passe alterada.')
      navegar('/', { replace: true })
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível alterar a palavra-passe.')
    }
  })

  const conteudo = (
    <div className="cartao mx-auto w-full max-w-md p-6">
      <h1 className="text-lg font-semibold">Alterar palavra-passe</h1>
      {obrigatorio && (
        <div className="mt-3">
          <Alerta tipo="aviso">Por segurança, defina uma nova palavra-passe antes de continuar.</Alerta>
        </div>
      )}
      <form onSubmit={submeter} noValidate className="mt-4 space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        {(['palavraPasseActual', 'novaPalavraPasse', 'confirmacao'] as const).map((campo) => (
          <div key={campo}>
            <label htmlFor={campo} className="rotulo">
              {{ palavraPasseActual: 'Palavra-passe actual', novaPalavraPasse: 'Nova palavra-passe', confirmacao: 'Confirmar nova palavra-passe' }[campo]}
            </label>
            <input
              id={campo}
              type="password"
              autoComplete={campo === 'palavraPasseActual' ? 'current-password' : 'new-password'}
              className={`campo ${formState.errors[campo] ? 'campo-erro' : ''}`}
              {...register(campo)}
            />
            <ErroCampo mensagem={formState.errors[campo]?.message} />
          </div>
        ))}
        <p className="text-xs text-slate-500">Mínimo 8 caracteres, com pelo menos uma letra minúscula e um algarismo.</p>
        <div className="flex justify-end gap-2">
          {obrigatorio ? (
            <button type="button" className="btn-secundario" onClick={() => void sair()}>Terminar sessão</button>
          ) : (
            <button type="button" className="btn-secundario" onClick={() => navegar(-1)}>Cancelar</button>
          )}
          <button type="submit" className="btn-primario" disabled={formState.isSubmitting || !podeGravar}>
            {formState.isSubmitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
            Guardar
          </button>
        </div>
      </form>
    </div>
  )

  // Quando obrigatório, mostra-se sem o menu (o utilizador ainda não pode usar outras áreas).
  return obrigatorio ? <div className="flex min-h-full items-center justify-center bg-slate-100 p-4">{conteudo}</div> : conteudo
}
