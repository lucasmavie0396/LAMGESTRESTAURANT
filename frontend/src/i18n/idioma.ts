/** Idioma do ecrã: Português (padrão) ou Inglês. Escolhido em cada computador/tablet (fica no navegador), como o tema. */
export type Idioma = 'pt' | 'en'

const CHAVE = 'lamgest.idioma'

export const NOMES_IDIOMA: Record<Idioma, string> = { pt: 'Português', en: 'English' }

export function idiomaGuardado(): Idioma {
  try {
    return localStorage.getItem(CHAVE) === 'en' ? 'en' : 'pt'
  } catch {
    return 'pt'
  }
}

/** Idioma em uso nesta página (fixo até recarregar). */
export const idiomaActual: Idioma = typeof window === 'undefined' ? 'pt' : idiomaGuardado()

/** Guarda o idioma e recarrega a página, para todo o ecrã mudar de uma só vez. */
export function definirIdioma(idioma: Idioma) {
  if (idioma === idiomaActual) return
  try {
    localStorage.setItem(CHAVE, idioma)
  } catch {
    // sem armazenamento: não há como lembrar a escolha
    return
  }
  window.location.reload()
}

/** Locale das datas com nomes (dias e meses). Os números e a moeda mantêm o formato de Moçambique em ambos os idiomas. */
export const localeDatas = () => (idiomaActual === 'en' ? 'en-GB' : 'pt-MZ')
