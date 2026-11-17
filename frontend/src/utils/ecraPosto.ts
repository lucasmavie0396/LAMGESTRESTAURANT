/**
 * Ecrã do posto (Configurações → Postos): modo táctil (botões e letra maiores) e tamanho do sistema em %.
 * O servidor devolve-o no sinal de presença do posto; fica guardado neste navegador para se aplicar logo ao abrir, antes do login.
 * O tamanho muda a letra base da página: como o Tailwind mede tudo em rem, o sistema inteiro cresce ou encolhe na mesma proporção.
 */
export interface EcraPosto { tactil: boolean; escala: number }

export const ESCALAS = [90, 100, 110, 125] as const
export const ECRA_NORMAL: EcraPosto = { tactil: false, escala: 100 }
const CHAVE = 'lamgest.ecraPosto'

export function lerEcraPosto(): EcraPosto {
  try {
    const v = JSON.parse(localStorage.getItem(CHAVE) ?? 'null') as Partial<EcraPosto> | null
    if (!v) return ECRA_NORMAL
    return { tactil: v.tactil === true, escala: ESCALAS.includes(v.escala as never) ? v.escala! : 100 }
  } catch {
    return ECRA_NORMAL
  }
}

export function aplicarEcraPosto(ecra: EcraPosto = lerEcraPosto()) {
  const html = document.documentElement
  html.style.fontSize = ecra.escala === 100 ? '' : `${ecra.escala}%`
  if (ecra.tactil) html.dataset.tactil = '1'
  else delete html.dataset.tactil
}

/** Guarda e aplica o ecrã recebido do servidor (só se mudou). */
export function definirEcraPosto(ecra: EcraPosto) {
  const actual = lerEcraPosto()
  if (actual.tactil === ecra.tactil && actual.escala === ecra.escala) return
  try {
    localStorage.setItem(CHAVE, JSON.stringify({ tactil: ecra.tactil, escala: ecra.escala }))
  } catch { /* sem armazenamento: aplica só nesta página */ }
  aplicarEcraPosto(ecra)
}

/** Descrição curta para o cartão do posto. */
export const descreverEcra = (e: EcraPosto) => `${e.tactil ? 'modo táctil' : 'normal'} · tamanho ${e.escala}%`
