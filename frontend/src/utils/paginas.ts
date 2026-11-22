import { lazy, type ComponentType } from 'react'

/** Chave (por sessão do separador) que evita recarregar sem fim quando um módulo não carrega mesmo depois de recarregar. */
const CHAVE_RECARGA = 'lamgest.recarga-modulo'

/**
 * Um módulo que não carrega porque o servidor foi actualizado (os ficheiros antigos deixaram de existir) recarrega a
 * página uma vez, para ir buscar a versão nova; se voltar a falhar, o erro segue para a protecção de erros do ecrã.
 */
async function comRecarga<T>(carregar: () => Promise<T>): Promise<T> {
  try {
    const m = await carregar()
    try { sessionStorage.removeItem(CHAVE_RECARGA) } catch { /* sem armazenamento */ }
    return m
  } catch (erro) {
    let jaRecarregou = true
    try {
      jaRecarregou = sessionStorage.getItem(CHAVE_RECARGA) === '1'
      if (!jaRecarregou) sessionStorage.setItem(CHAVE_RECARGA, '1')
    } catch { /* sem armazenamento: não arrisca recarregar em ciclo */ }
    if (!jaRecarregou) {
      location.reload()
      return new Promise<T>(() => {})
    }
    throw erro
  }
}

export type PaginaPreguicosa = ComponentType & { preparar: () => void }

/**
 * Página carregada só quando é aberta (o primeiro carregamento fica mais pequeno, sobretudo no PDA e em postos fracos).
 * `preparar()` descarrega-a antecipadamente (ex.: o POS logo depois de entrar).
 */
export function pagina<M>(carregar: () => Promise<M>, escolher: (m: M) => ComponentType): PaginaPreguicosa {
  let promessa: Promise<{ default: ComponentType }> | null = null
  const obter = () => (promessa ??= comRecarga(carregar).then((m) => ({ default: escolher(m) })).catch((e) => {
    promessa = null
    throw e
  }))
  const Componente = lazy(obter) as unknown as PaginaPreguicosa
  Componente.preparar = () => { void obter().catch(() => {}) }
  return Componente
}
