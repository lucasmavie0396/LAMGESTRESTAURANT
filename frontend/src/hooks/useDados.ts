import { useCallback, useEffect, useRef, useState, type DependencyList } from 'react'
import { ErroApi } from '../api/cliente'

export function mensagemErro(e: unknown, padrao = 'A operação falhou.') {
  return e instanceof ErroApi ? e.message : padrao
}

/**
 * Carrega dados da API e recarrega quando as dependências mudam.
 * Ignora respostas de pedidos antigos (evita que uma pesquisa lenta sobreponha a mais recente).
 */
export function useDados<T>(carregar: () => Promise<T>, deps: DependencyList) {
  const [dados, setDados] = useState<T | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [aCarregar, setACarregar] = useState(true)
  const pedidoActual = useRef(0)

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const executar = useCallback(carregar, deps)

  const recarregar = useCallback(async () => {
    const id = ++pedidoActual.current
    setACarregar(true)
    try {
      const resultado = await executar()
      if (id === pedidoActual.current) {
        setDados(resultado)
        setErro(null)
      }
    } catch (e) {
      if (id === pedidoActual.current) setErro(mensagemErro(e, 'Não foi possível carregar os dados.'))
    } finally {
      if (id === pedidoActual.current) setACarregar(false)
    }
  }, [executar])

  useEffect(() => {
    void recarregar()
  }, [recarregar])

  return { dados, erro, aCarregar, recarregar, setDados }
}
