import type { Artigo } from '../../types/api'
import { agruparArtigos } from './comum'

/** Opções de uma lista de artigos agrupadas por categoria (sem categorias, lista simples). */
export function OpcoesArtigos({ artigos }: { artigos: Artigo[] }) {
  const grupos = agruparArtigos(artigos)
  const opcao = (a: Artigo) => <option key={a.id} value={a.id}>{a.nome} ({a.unidade})</option>
  if (grupos.length === 1 && grupos[0][0] === 'Sem categoria') return <>{grupos[0][1].map(opcao)}</>
  return <>{grupos.map(([g, lista]) => <optgroup key={g} label={g}>{lista.map(opcao)}</optgroup>)}</>
}
