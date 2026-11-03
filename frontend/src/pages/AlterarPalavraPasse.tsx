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
import { erroPin, regraPalavraPasse } from '../utils/validacao'

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
  return obrigatorio
    ? <div className="flex min-h-full items-center justify-center bg-slate-100 p-4">{conteudo}</div>
    : <div className="space-y-4">{conteudo}<MeuCodigo /></div>
}

/** O próprio utilizador muda o seu código do ecrã táctil (confirma com a palavra-passe). */
function MeuCodigo() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [palavraPasse, setPalavraPasse] = useState('')
  const [pin, setPin] = useState('')
  const [confirmacao, setConfirmacao] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const erroLocal = !palavraPasse ? 'Indique a palavra-passe actual.' : erroPin(pin) ?? (pin !== confirmacao ? 'Os códigos não coincidem.' : null)

  const guardar = async () => {
    setErro(null)
    setAGuardar(true)
    try {
      await api.post('/api/auth/alterar-pin', { palavraPasseActual: palavraPasse, novoPin: pin })
      avisos.sucesso('Código do ecrã táctil alterado.')
      setPalavraPasse(''); setPin(''); setConfirmacao('')
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível alterar o código.')
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <div className="cartao mx-auto w-full max-w-md p-6">
      <h2 className="text-lg font-semibold">Código do ecrã táctil</h2>
      <p className="mt-1 text-sm text-slate-600">Para entrar nos postos com o ecrã táctil (4 a 6 algarismos). Só funciona se o administrador o activou para si.</p>
      <div className="mt-4 space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div>
          <label htmlFor="pinPalavraPasse" className="rotulo">Palavra-passe actual</label>
          <input id="pinPalavraPasse" type="password" autoComplete="current-password" className="campo" value={palavraPasse} onChange={(e) => setPalavraPasse(e.target.value)} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <label htmlFor="meuPin" className="rotulo">Novo código</label>
            <input id="meuPin" type="password" inputMode="numeric" autoComplete="new-password" maxLength={6} className="campo tracking-widest"
              value={pin} onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))} />
          </div>
          <div>
            <label htmlFor="meuPinConfirmar" className="rotulo">Confirmar código</label>
            <input id="meuPinConfirmar" type="password" inputMode="numeric" autoComplete="new-password" maxLength={6} className="campo tracking-widest"
              value={confirmacao} onChange={(e) => setConfirmacao(e.target.value.replace(/\D/g, ''))} />
          </div>
        </div>
        {pin !== '' && erroLocal && <p className="text-sm text-amber-700">{erroLocal}</p>}
        <div className="flex justify-end">
          <button type="button" className="btn-primario" disabled={aGuardar || !podeGravar || !!erroLocal} onClick={() => void guardar()}>
            {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar código
          </button>
        </div>
      </div>
    </div>
  )
}
