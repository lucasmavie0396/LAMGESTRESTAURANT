import { useCallback, useEffect, useRef, useState } from 'react'
import { api, ErroApi, query } from '../../api/cliente'
import { mensagemErro } from '../../hooks/useDados'
import type { Pagina, Produto, ProdutoEtiqueta } from '../../types/api'

/** Entre teclas do leitor de código de barras: margem para computadores lentos; à mão não se escreve um código com Enter fora de um campo. */
const INTERVALO_LEITOR_MS = 200

interface Opcoes {
  /** Leitor ligado nas configurações do POS e servidor disponível. */
  activo: boolean
  /** A gravar ou com um diálogo aberto: os códigos esperam na fila. */
  ocupado: boolean
  /** Com um diálogo aberto o teclado é do diálogo: o leitor não escuta (a gravar continua a escutar e a pôr em fila). */
  comDialogo: boolean
  /** Produto encontrado (com a quantidade da etiqueta da balança, se for o caso). */
  aoEncontrar: (produto: Produto, quantidade?: number) => void
  /** O código veio do campo de pesquisa: limpa-o depois de lançar o produto. */
  aoLimparPesquisa: () => void
  aoErro: (mensagem: string) => void
}

/**
 * Leitor de código de barras do POS: o leitor escreve como um teclado muito rápido e termina com Enter (fora dos campos de
 * texto e dos diálogos). Os códigos ficam em fila e tratam-se um de cada vez (leituras seguidas não se perdem); um código
 * EAN-13 que não seja de um produto é tentado como etiqueta da balança (peso ou preço).
 * Devolve `receberCodigo`, para o campo de pesquisa e a câmara mandarem códigos para a mesma fila.
 */
export function useLeitorCodigos({ activo, ocupado, comDialogo, aoEncontrar, aoLimparPesquisa, aoErro }: Opcoes) {
  // As funções mudam a cada desenho do POS: a fila usa sempre as mais recentes.
  const accoes = useRef({ aoEncontrar, aoLimparPesquisa, aoErro })
  useEffect(() => { accoes.current = { aoEncontrar, aoLimparPesquisa, aoErro } })

  const filaCodigos = useRef<{ codigo: string; daPesquisa: boolean }[]>([])
  const aProcurar = useRef(false)
  const [codigosRecebidos, setCodigosRecebidos] = useState(0)
  const receberCodigo = useCallback((codigo: string, daPesquisa = false) => {
    filaCodigos.current.push({ codigo, daPesquisa })
    setCodigosRecebidos((n) => n + 1)
  }, [])

  useEffect(() => {
    if (ocupado || aProcurar.current || filaCodigos.current.length === 0) return
    const { codigo, daPesquisa } = filaCodigos.current.shift()!
    const { aoEncontrar: encontrar, aoLimparPesquisa: limpar, aoErro: erro } = accoes.current
    aProcurar.current = true
    api.get<Pagina<Produto>>('/api/produtos' + query({ activo: true, pos: true, pesquisa: codigo, tamanho: 20 }))
      .then(async (r) => {
        const produto = r.itens.find((x) => x.codigoBarras === codigo || x.codigo === codigo)
        if (produto) {
          if (daPesquisa) limpar()
          return encontrar(produto)
        }
        // Etiqueta da balança (EAN-13 com peso ou preço): o servidor descodifica e escolhe o produto.
        if (/^\d{13}$/.test(codigo)) {
          try {
            const etiqueta = await api.get<ProdutoEtiqueta>(`/api/balanca/etiqueta/${codigo}`)
            if (daPesquisa) limpar()
            return encontrar(etiqueta.produto, etiqueta.quantidade)
          } catch (e) {
            if (!(e instanceof ErroApi && e.estado === 404)) return erro(mensagemErro(e))
          }
        }
        if (!daPesquisa) erro(`Código ${codigo} não encontrado.`)
      })
      .catch((e) => erro(mensagemErro(e)))
      .finally(() => {
        aProcurar.current = false
        setCodigosRecebidos((n) => n + 1) // passa ao código seguinte
      })
  }, [codigosRecebidos, ocupado])

  useEffect(() => {
    if (!activo || comDialogo) return
    let lido = ''
    let ultima = 0
    const aoTeclar = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement | null)?.closest?.('input, textarea, select') || e.ctrlKey || e.altKey || e.metaKey) return
      const rapido = e.timeStamp - ultima < INTERVALO_LEITOR_MS
      if (e.key === 'Enter') {
        if (lido.length >= 3 && rapido) {
          e.preventDefault()
          receberCodigo(lido)
        }
        lido = ''
      } else if (e.key.length === 1) {
        lido = rapido ? lido + e.key : e.key
        ultima = e.timeStamp
      }
    }
    window.addEventListener('keydown', aoTeclar, true)
    // Sinal para os testes automáticos (e para diagnóstico): o leitor está a escutar.
    document.body.dataset.leitorCodigos = 'activo'
    return () => {
      window.removeEventListener('keydown', aoTeclar, true)
      delete document.body.dataset.leitorCodigos
    }
  }, [activo, comDialogo, receberCodigo])

  return receberCodigo
}
