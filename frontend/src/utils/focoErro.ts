import { ErroApi } from '../api/cliente'

/**
 * Põe o cursor no campo do erro: o servidor indica o campo em «errors» (ex.: unidadeMedidaId) e o mapa diz o id do
 * controlo no ecrã. Devolve o campo focado (ou null).
 */
export function focarCampoDoErro(erro: unknown, ids: Record<string, string>): string | null {
  if (!(erro instanceof ErroApi)) return null
  const campo = Object.keys(erro.erros).find((c) => ids[c])
  if (!campo) return null
  focarCampo(ids[campo])
  return campo
}

/** Foca (e selecciona o texto de) um controlo pelo id, depois de o ecrã actualizar. */
export function focarCampo(id: string) {
  setTimeout(() => {
    const el = document.getElementById(id)
    if (!(el instanceof HTMLElement)) return
    el.focus()
    if (el instanceof HTMLInputElement && el.type !== 'checkbox') el.select()
  }, 0)
}
