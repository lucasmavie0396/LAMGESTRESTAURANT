import { useCallback, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { ArrowLeft, Check, Delete, KeyRound, Loader2, LogIn, RefreshCw, UsersRound } from 'lucide-react'
import { api, ErroApi } from '../api/cliente'
import { Alerta, ErroCampo } from '../components/ui'
import { DESCRICAO_LIGACAO, useLigacao } from '../contexts/LigacaoContext'
import { useSessao } from '../contexts/SessaoContext'
import { mensagemErro, useDados } from '../hooks/useDados'
import type { SessaoUtilizador, UtilizadorTactil } from '../types/api'
import { guardarLoginTactil, lerLoginTactil } from '../utils/terminalLocal'

const esquema = z.object({
  nomeUtilizador: z.string().trim().min(1, 'Indique o nome de utilizador.'),
  palavraPasse: z.string().min(1, 'Indique a palavra-passe.'),
})
type Dados = z.infer<typeof esquema>

/**
 * Entrada no sistema. Postos com o ecrã táctil activo (atalho do instalador com «tactil=1», ou escolhido aqui)
 * mostram os botões dos utilizadores e um teclado numérico para o código; o formulário de palavra-passe
 * continua disponível (o Administrador entra sempre com a palavra-passe).
 */
export function Login() {
  const { utilizador } = useSessao()
  const navegar = useNavigate()
  const local = useLocation()
  const [tactil, setTactil] = useState(lerLoginTactil)
  const { licenca } = useLigacao()

  if (utilizador) return <Navigate to="/" replace />

  const entrou = (sessao: SessaoUtilizador) => {
    const destino = (local.state as { de?: string } | null)?.de ?? '/'
    navegar(sessao.deveAlterarPalavraPasse ? '/alterar-palavra-passe' : destino, { replace: true })
  }

  return (
    <div className="flex min-h-full flex-col items-center justify-center gap-4 bg-gradient-to-br from-primaria-800 to-primaria-600 p-4">
      {licenca && licenca.estado !== 'Valida' && (
        <div role="alert" className={`w-full max-w-4xl rounded-xl px-4 py-3 text-center text-sm font-medium ${licenca.bloqueada || licenca.estado === 'Tolerancia' ? 'bg-red-600 text-white' : 'bg-amber-100 text-amber-900'}`}>
          {licenca.mensagem}{licenca.bloqueada && ' Pode entrar para consultar; o administrador activa a licença em Configurações → Licença.'}
        </div>
      )}
      {tactil
        ? <EcraTactil aoEntrar={entrou} aoUsarPalavraPasse={() => setTactil(false)} />
        : <FormularioPalavraPasse aoEntrar={entrou} aoUsarTactil={() => { guardarLoginTactil(true); setTactil(true) }} />}
    </div>
  )
}

function Marca() {
  return (
    <div className="flex items-center gap-3">
      <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-primaria-600 text-lg font-bold text-white">LG</div>
      <div>
        <h1 className="text-xl font-semibold">LAMGest</h1>
        <p className="text-sm text-slate-500">Restaurant ERP</p>
      </div>
    </div>
  )
}

function FormularioPalavraPasse({ aoEntrar, aoUsarTactil }: { aoEntrar: (s: SessaoUtilizador) => void; aoUsarTactil: () => void }) {
  const { entrar } = useSessao()
  const { estado } = useLigacao()
  const podeGravar = estado === 'ligado'
  const [erro, setErro] = useState<string | null>(null)
  const { register, handleSubmit, formState } = useForm<Dados>({ resolver: zodResolver(esquema) })

  const submeter = handleSubmit(async (dados) => {
    setErro(null)
    try {
      aoEntrar(await entrar(dados.nomeUtilizador, dados.palavraPasse))
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível iniciar sessão.')
    }
  })

  return (
    <div className="cartao w-full max-w-sm p-8">
      <div className="mb-6"><Marca /></div>

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
      <button type="button" className="btn-secundario mt-4 w-full" onClick={aoUsarTactil}>
        <UsersRound className="h-4 w-4" aria-hidden /> Ecrã táctil (entrar com código)
      </button>
      <p className="mt-6 text-center text-xs text-slate-500">Esqueceu a palavra-passe? Peça ao administrador para a repor.</p>
    </div>
  )
}

const iniciais = (nome: string) => nome.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('')

function EcraTactil({ aoEntrar, aoUsarPalavraPasse }: { aoEntrar: (s: SessaoUtilizador) => void; aoUsarPalavraPasse: () => void }) {
  const { estado } = useLigacao()
  const podeGravar = estado === 'ligado'
  const lista = useDados(() => api.get<UtilizadorTactil[]>('/api/auth/tactil'), [])
  const [escolhido, setEscolhido] = useState<UtilizadorTactil | null>(null)

  return (
    <div className="cartao w-full max-w-4xl p-6 sm:p-8">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <Marca />
        <div className="flex gap-2">
          {!escolhido && (
            <button type="button" className="btn-secundario" onClick={() => void lista.recarregar()} aria-label="Actualizar a lista">
              <RefreshCw className="h-4 w-4" aria-hidden />
            </button>
          )}
          <button type="button" className="btn-secundario" onClick={aoUsarPalavraPasse}>
            <KeyRound className="h-4 w-4" aria-hidden /> Entrar com palavra-passe
          </button>
        </div>
      </div>
      {!podeGravar && estado !== 'a-verificar' && <div className="mb-4"><Alerta tipo="erro">{DESCRICAO_LIGACAO[estado]}. Não é possível iniciar sessão.</Alerta></div>}

      {escolhido ? (
        <TecladoCodigo utilizador={escolhido} desactivado={!podeGravar} aoEntrar={aoEntrar} aoVoltar={() => setEscolhido(null)} />
      ) : (
        <>
          <h2 className="mb-3 text-lg font-semibold">Quem vai entrar?</h2>
          {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
          {lista.aCarregar && !lista.dados && <Loader2 className="h-6 w-6 animate-spin text-primaria-600" aria-label="A carregar" />}
          {lista.dados?.length === 0 && (
            <Alerta tipo="info">Ainda não há utilizadores com código. O administrador define-o em Utilizadores → «Código do ecrã táctil».</Alerta>
          )}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {lista.dados?.map((u) => (
              <button key={u.id} type="button" onClick={() => setEscolhido(u)} disabled={!podeGravar}
                className="flex min-h-32 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-slate-200 bg-white p-3 text-center shadow-sm transition hover:border-primaria-500 active:scale-[0.98] disabled:opacity-50">
                <span className="flex h-14 w-14 items-center justify-center rounded-full bg-primaria-600 text-xl font-bold text-white" aria-hidden>{iniciais(u.nome)}</span>
                <span className="font-semibold leading-tight">{u.nome}</span>
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

const TECLAS = ['1', '2', '3', '4', '5', '6', '7', '8', '9']

function TecladoCodigo({ utilizador, desactivado, aoEntrar, aoVoltar }: {
  utilizador: UtilizadorTactil
  desactivado: boolean
  aoEntrar: (s: SessaoUtilizador) => void
  aoVoltar: () => void
}) {
  const { entrarComPin } = useSessao()
  const [codigo, setCodigo] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aEntrar, setAEntrar] = useState(false)

  const premir = useCallback((d: string) => { setErro(null); setCodigo((c) => (c.length < 6 ? c + d : c)) }, [])
  const apagar = useCallback(() => setCodigo((c) => c.slice(0, -1)), [])

  const confirmar = useCallback(async () => {
    if (codigo.length < 4 || aEntrar || desactivado) return
    setAEntrar(true)
    try {
      aoEntrar(await entrarComPin(utilizador.id, codigo))
    } catch (e) {
      setErro(mensagemErro(e, 'Não foi possível iniciar sessão.'))
      setCodigo('')
    } finally {
      setAEntrar(false)
    }
  }, [codigo, aEntrar, desactivado, aoEntrar, entrarComPin, utilizador.id])

  // Também aceita o teclado físico (algarismos, apagar, Enter, Esc).
  useEffect(() => {
    const tecla = (e: KeyboardEvent) => {
      if (/^\d$/.test(e.key)) premir(e.key)
      else if (e.key === 'Backspace') apagar()
      else if (e.key === 'Enter') void confirmar()
      else if (e.key === 'Escape') aoVoltar()
    }
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [premir, apagar, confirmar, aoVoltar])

  const classeTecla = 'flex h-16 items-center justify-center rounded-2xl border-2 border-slate-200 bg-white text-2xl font-semibold shadow-sm active:scale-[0.97] active:bg-slate-100 disabled:opacity-50'

  return (
    <div className="mx-auto max-w-xs">
      <button type="button" className="mb-4 inline-flex items-center gap-1 text-sm font-medium text-primaria-700" onClick={aoVoltar}>
        <ArrowLeft className="h-4 w-4" aria-hidden /> Outro utilizador
      </button>
      <div className="mb-4 text-center">
        <span className="mx-auto mb-2 flex h-16 w-16 items-center justify-center rounded-full bg-primaria-600 text-2xl font-bold text-white" aria-hidden>{iniciais(utilizador.nome)}</span>
        <p className="text-lg font-semibold">{utilizador.nome}</p>
        <p className="text-sm text-slate-500">Introduza o seu código</p>
      </div>
      <div className="mb-4 flex h-8 justify-center gap-3" role="status" aria-label={`${codigo.length} algarismos introduzidos`}>
        {Array.from({ length: Math.max(4, codigo.length) }, (_, i) => (
          <span key={i} className={`h-4 w-4 self-center rounded-full ${i < codigo.length ? 'bg-primaria-600' : 'border-2 border-slate-300'}`} />
        ))}
      </div>
      {erro && <div className="mb-3"><Alerta tipo="erro">{erro}</Alerta></div>}
      <div className="grid grid-cols-3 gap-3" aria-label="Teclado numérico">
        {TECLAS.map((d) => (
          <button key={d} type="button" className={classeTecla} onClick={() => premir(d)} disabled={aEntrar}>{d}</button>
        ))}
        <button type="button" className={classeTecla} onClick={apagar} disabled={aEntrar || codigo.length === 0} aria-label="Apagar">
          <Delete className="h-6 w-6" aria-hidden />
        </button>
        <button type="button" className={classeTecla} onClick={() => premir('0')} disabled={aEntrar}>0</button>
        <button type="button" className={`${classeTecla} border-primaria-600 bg-primaria-600 text-white active:bg-primaria-700`}
          onClick={() => void confirmar()} disabled={aEntrar || desactivado || codigo.length < 4} aria-label="Entrar">
          {aEntrar ? <Loader2 className="h-6 w-6 animate-spin" aria-hidden /> : <Check className="h-7 w-7" aria-hidden />}
        </button>
      </div>
    </div>
  )
}
