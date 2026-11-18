import { useSyncExternalStore } from 'react'

/** Tema do ecrã: claro ou escuro. Escolhido em cada computador/tablet (fica no navegador). */
export type Tema = 'claro' | 'escuro'

const CHAVE = 'lamgest.tema'
const ouvintes = new Set<() => void>()

/** Tema guardado neste navegador; na primeira vez, o do Windows/sistema. */
export function temaGuardado(): Tema {
  try {
    const t = localStorage.getItem(CHAVE)
    if (t === 'claro' || t === 'escuro') return t
  } catch {
    // armazenamento indisponível (janela privada, etc.)
  }
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'escuro' : 'claro'
}

/** Aplica o tema à página (classe «escuro» em <html>, que troca as cores de src/tema.css). */
export function aplicarTema(tema: Tema = temaGuardado()) {
  document.documentElement.classList.toggle('escuro', tema === 'escuro')
}

export function definirTema(tema: Tema) {
  try {
    localStorage.setItem(CHAVE, tema)
  } catch {
    // sem armazenamento: vale só até recarregar
  }
  aplicarTema(tema)
  ouvintes.forEach((o) => o())
}

const temaActual = (): Tema => (document.documentElement.classList.contains('escuro') ? 'escuro' : 'claro')

/** Tema actual e a troca (botão ☀/☾ da barra de cima). */
export function useTema() {
  const tema = useSyncExternalStore(
    (o) => {
      ouvintes.add(o)
      return () => ouvintes.delete(o)
    },
    temaActual,
    () => 'claro' as Tema,
  )
  return { tema, escuro: tema === 'escuro', alternar: () => definirTema(tema === 'escuro' ? 'claro' : 'escuro') }
}
