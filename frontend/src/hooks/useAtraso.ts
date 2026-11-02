import { useEffect, useState } from 'react'

/** Devolve o valor só depois de deixar de mudar durante `ms` (ex.: pesquisa ao escrever). */
export function useAtraso<T>(valor: T, ms = 300): T {
  const [atrasado, setAtrasado] = useState(valor)
  useEffect(() => {
    const id = window.setTimeout(() => setAtrasado(valor), ms)
    return () => window.clearTimeout(id)
  }, [valor, ms])
  return atrasado
}
