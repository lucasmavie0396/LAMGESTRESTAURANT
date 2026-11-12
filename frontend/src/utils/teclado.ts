import { emPda } from './pda'

/** Teclado no ecrã deste posto: automático (só em ecrãs tácteis), sempre ou nunca. Guardado neste navegador. */
export type ModoTeclado = 'auto' | 'sempre' | 'nunca'

const CHAVE = 'lamgest.teclado'
const EVENTO = 'lamgest-teclado'

export const NOMES_MODO_TECLADO: Record<ModoTeclado, string> = { auto: 'Automático', sempre: 'Sempre', nunca: 'Nunca' }

/** Nos tablets/PDA (app Android) o próprio aparelho já tem teclado: por omissão, nunca. */
export function lerModoTeclado(): ModoTeclado {
  try {
    const v = localStorage.getItem(CHAVE)
    if (v === 'auto' || v === 'sempre' || v === 'nunca') return v
  } catch {
    // sem armazenamento: valor por omissão
  }
  return emPda() ? 'nunca' : 'auto'
}

export function guardarModoTeclado(modo: ModoTeclado) {
  try {
    localStorage.setItem(CHAVE, modo)
  } catch {
    // sem armazenamento: vale só nesta página
  }
  window.dispatchEvent(new CustomEvent(EVENTO, { detail: modo }))
}

export function aoMudarModoTeclado(ouvinte: (modo: ModoTeclado) => void) {
  const f = (e: Event) => ouvinte((e as CustomEvent<ModoTeclado>).detail)
  window.addEventListener(EVENTO, f)
  return () => window.removeEventListener(EVENTO, f)
}

/** Ecrã táctil sem rato: o ponteiro principal é o dedo. */
export const ecraTactil = () => typeof window !== 'undefined' && !!window.matchMedia?.('(pointer: coarse)').matches

const TIPOS_TEXTO = new Set(['text', 'search', 'tel', 'email', 'password', 'url', 'number'])

/** Campo de texto que recebe o teclado no ecrã (não os só de leitura, datas, caixas de escolha ou marcados com data-sem-teclado). */
export function campoComTeclado(el: EventTarget | null): el is HTMLInputElement | HTMLTextAreaElement {
  if (el instanceof HTMLTextAreaElement) return !el.readOnly && !el.disabled && !el.dataset.semTeclado
  if (!(el instanceof HTMLInputElement)) return false
  return TIPOS_TEXTO.has(el.type) && !el.readOnly && !el.disabled && !el.dataset.semTeclado
}

/** Campos de valores: teclado numérico. */
export const campoNumerico = (el: HTMLInputElement | HTMLTextAreaElement) =>
  (el instanceof HTMLInputElement && el.type === 'number') || ['decimal', 'numeric', 'tel'].includes(el.inputMode)

/**
 * Escreve no campo como se fosse o teclado: usa o «setter» nativo e o evento input, para o React ver a alteração.
 * Respeita o cursor/selecção e o maxLength. `apagar`: apaga a selecção ou o carácter antes do cursor.
 */
export function escreverNoCampo(el: HTMLInputElement | HTMLTextAreaElement, texto: string, apagar = false) {
  const valor = el.value
  let inicio = valor.length
  let fim = valor.length
  try {
    if (el.selectionStart !== null && el.selectionEnd !== null) {
      inicio = el.selectionStart
      fim = el.selectionEnd
    }
  } catch {
    // campos numéricos não têm cursor: escreve no fim
  }
  if (apagar && inicio === fim) inicio = Math.max(0, inicio - 1)
  const novo = valor.slice(0, inicio) + (apagar ? '' : texto) + valor.slice(fim)
  if (el.maxLength > 0 && novo.length > el.maxLength) return
  const prototipo = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(prototipo, 'value')?.set?.call(el, novo)
  el.dispatchEvent(new Event('input', { bubbles: true }))
  const cursor = inicio + (apagar ? 0 : texto.length)
  try {
    el.setSelectionRange(cursor, cursor)
  } catch {
    // campos numéricos
  }
}
