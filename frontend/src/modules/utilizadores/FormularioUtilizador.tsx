import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Loader2 } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { Alerta, ErroCampo, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import type { PerfilResumo, UtilizadorResumo } from '../../types/api'
import { regraNomeUtilizador, regraPalavraPasse } from '../../utils/validacao'

const esquemaBase = z.object({
  nomeCompleto: z.string().trim().min(1, 'Indique o nome completo.').max(200, 'Máximo 200 caracteres.'),
  perfis: z.array(z.string()).min(1, 'Seleccione pelo menos um perfil.'),
})
const esquemaNovo = esquemaBase.extend({
  nomeUtilizador: regraNomeUtilizador,
  palavraPasseTemporaria: regraPalavraPasse,
})
type Dados = z.infer<typeof esquemaNovo>

/** Criação e edição. Utilizadores novos recebem uma palavra-passe temporária e são obrigados a alterá-la. */
export function FormularioUtilizador({
  utilizador,
  perfis,
  aoFechar,
  aoGuardar,
}: {
  utilizador?: UtilizadorResumo
  perfis: PerfilResumo[]
  aoFechar: () => void
  aoGuardar: () => void
}) {
  const edicao = Boolean(utilizador)
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [erro, setErro] = useState<string | null>(null)
  const { register, handleSubmit, formState, setError } = useForm<Dados>({
    // Na edição não há nome de utilizador nem palavra-passe; o esquema base chega.
    resolver: zodResolver(edicao ? (esquemaBase as unknown as typeof esquemaNovo) : esquemaNovo),
    defaultValues: {
      nomeCompleto: utilizador?.nomeCompleto ?? '',
      perfis: utilizador?.perfis ?? [],
      nomeUtilizador: '',
      palavraPasseTemporaria: '',
    },
  })

  const submeter = handleSubmit(async (d) => {
    setErro(null)
    try {
      if (utilizador) {
        await api.put(`/api/utilizadores/${utilizador.id}`, { nomeCompleto: d.nomeCompleto, perfis: d.perfis })
        avisos.sucesso('Utilizador actualizado.')
      } else {
        await api.post('/api/utilizadores', d)
        avisos.sucesso('Utilizador criado. Comunique-lhe a palavra-passe temporária.')
      }
      aoGuardar()
    } catch (e) {
      if (e instanceof ErroApi) {
        for (const [campo, mensagens] of Object.entries(e.erros))
          setError(campo as keyof Dados, { message: mensagens[0] })
        setErro(e.message)
      } else {
        setErro('Não foi possível guardar.')
      }
    }
  })

  return (
    <Modal
      titulo={edicao ? `Editar ${utilizador!.nomeCompleto}` : 'Novo utilizador'}
      aoFechar={aoFechar}
      rodape={
        <>
          <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="submit" form="form-utilizador" className="btn-primario" disabled={formState.isSubmitting || !podeGravar}>
            {formState.isSubmitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
            Guardar
          </button>
        </>
      }
    >
      <form id="form-utilizador" onSubmit={submeter} noValidate className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        {!edicao && (
          <div>
            <label htmlFor="nomeUtilizador" className="rotulo">Nome de utilizador</label>
            <input id="nomeUtilizador" autoComplete="off" className={`campo ${formState.errors.nomeUtilizador ? 'campo-erro' : ''}`} {...register('nomeUtilizador')} />
            <ErroCampo mensagem={formState.errors.nomeUtilizador?.message} />
          </div>
        )}
        <div>
          <label htmlFor="nomeCompleto" className="rotulo">Nome completo</label>
          <input id="nomeCompleto" className={`campo ${formState.errors.nomeCompleto ? 'campo-erro' : ''}`} {...register('nomeCompleto')} />
          <ErroCampo mensagem={formState.errors.nomeCompleto?.message} />
        </div>
        {!edicao && (
          <div>
            <label htmlFor="palavraPasseTemporaria" className="rotulo">Palavra-passe temporária</label>
            <input id="palavraPasseTemporaria" type="password" autoComplete="new-password"
              className={`campo ${formState.errors.palavraPasseTemporaria ? 'campo-erro' : ''}`} {...register('palavraPasseTemporaria')} />
            <ErroCampo mensagem={formState.errors.palavraPasseTemporaria?.message} />
            <p className="mt-1 text-xs text-slate-500">O utilizador terá de a alterar no primeiro acesso.</p>
          </div>
        )}
        <fieldset>
          <legend className="rotulo">Perfis</legend>
          <div className="grid grid-cols-2 gap-2">
            {perfis.map((p) => (
              <label key={p.id} className="flex min-h-11 cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 text-sm hover:bg-slate-50">
                <input type="checkbox" value={p.nome} className="h-4 w-4" {...register('perfis')} />
                {p.nome}
              </label>
            ))}
          </div>
          <ErroCampo mensagem={formState.errors.perfis?.message} />
        </fieldset>
      </form>
    </Modal>
  )
}

const esquemaRepor = z
  .object({ novaPalavraPasse: regraPalavraPasse, confirmacao: z.string() })
  .refine((d) => d.novaPalavraPasse === d.confirmacao, { path: ['confirmacao'], message: 'As palavras-passe não coincidem.' })
type DadosRepor = z.infer<typeof esquemaRepor>

/** Reposição pelo administrador (não há recuperação por e-mail na rede local). Também desbloqueia a conta. */
export function ReporPalavraPasseModal({
  utilizador,
  aoFechar,
  aoGuardar,
}: {
  utilizador: UtilizadorResumo
  aoFechar: () => void
  aoGuardar: () => void
}) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [erro, setErro] = useState<string | null>(null)
  const { register, handleSubmit, formState } = useForm<DadosRepor>({ resolver: zodResolver(esquemaRepor) })

  const submeter = handleSubmit(async (d) => {
    setErro(null)
    try {
      await api.post(`/api/utilizadores/${utilizador.id}/repor-palavra-passe`, { novaPalavraPasse: d.novaPalavraPasse })
      avisos.sucesso('Palavra-passe reposta. O utilizador terá de a alterar no próximo acesso.')
      aoGuardar()
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível repor a palavra-passe.')
    }
  })

  return (
    <Modal
      titulo="Repor palavra-passe"
      aoFechar={aoFechar}
      rodape={
        <>
          <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="submit" form="form-repor" className="btn-primario" disabled={formState.isSubmitting || !podeGravar}>
            {formState.isSubmitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
            Repor
          </button>
        </>
      }
    >
      <form id="form-repor" onSubmit={submeter} noValidate className="space-y-4">
        <p className="text-sm text-slate-600">
          Nova palavra-passe temporária para <strong>{utilizador.nomeCompleto}</strong>. A conta é desbloqueada e o utilizador terá de a
          alterar no próximo acesso.
        </p>
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div>
          <label htmlFor="novaPalavraPasse" className="rotulo">Nova palavra-passe temporária</label>
          <input id="novaPalavraPasse" type="password" autoComplete="new-password" className={`campo ${formState.errors.novaPalavraPasse ? 'campo-erro' : ''}`} {...register('novaPalavraPasse')} />
          <ErroCampo mensagem={formState.errors.novaPalavraPasse?.message} />
        </div>
        <div>
          <label htmlFor="confirmacao" className="rotulo">Confirmar</label>
          <input id="confirmacao" type="password" autoComplete="new-password" className={`campo ${formState.errors.confirmacao ? 'campo-erro' : ''}`} {...register('confirmacao')} />
          <ErroCampo mensagem={formState.errors.confirmacao?.message} />
        </div>
      </form>
    </Modal>
  )
}
