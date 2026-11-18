import { useEffect, useState } from 'react'
import { Store } from 'lucide-react'
import { irParaLoja, lojasDisponiveis, type OpcaoLoja } from '../utils/lojas'

/**
 * Seletor de lojas (lojas do mesmo dono ligadas pela VPN). `entrada`: no ecrã de entrada, para todos (só mostra as lojas que
 * este aparelho/servidor conhece). `topo`: dentro do sistema; com `podeMudar` falso mostra só o nome da loja actual.
 * Sem outras lojas não aparece.
 */
export function SeletorLoja({ variante, podeMudar = true }: { variante: 'entrada' | 'topo'; podeMudar?: boolean }) {
  const [lojas, setLojas] = useState<OpcaoLoja[]>([])
  useEffect(() => {
    lojasDisponiveis().then(setLojas).catch(() => setLojas([]))
  }, [])
  const actual = lojas.find((l) => l.actual)
  if (!actual || lojas.length < 2) return null
  const cor = actual.cor ?? '#2563eb'

  if (variante === 'topo' && !podeMudar)
    return (
      <span className="hidden items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold text-white sm:inline-flex" style={{ backgroundColor: cor }} data-testid="loja-actual">
        <Store className="h-3.5 w-3.5" aria-hidden /> {actual.nome}
      </span>
    )

  return (
    <label className={`inline-flex items-center gap-2 rounded-full text-sm font-semibold ${variante === 'entrada' ? 'bg-superficie px-3 py-1.5 text-slate-800 shadow' : 'px-2.5 py-1 text-xs text-white'}`}
      style={variante === 'topo' ? { backgroundColor: cor } : undefined} title="Mudar de loja">
      <Store className="h-4 w-4" aria-hidden style={variante === 'entrada' ? { color: cor } : undefined} />
      <span className="sr-only">Loja</span>
      <select aria-label="Loja" data-sem-teclado className={`max-w-56 cursor-pointer rounded-md border-0 bg-transparent py-0.5 pl-1 pr-6 font-semibold focus:ring-2 ${variante === 'topo' ? 'text-white [&>option]:text-slate-900' : ''}`}
        value={actual.id} onChange={(e) => { const l = lojas.find((x) => x.id === e.target.value); if (l && !l.actual) irParaLoja(l) }}>
        {lojas.map((l) => <option key={l.id} value={l.id}>{l.actual ? `Loja: ${l.nome}` : l.nome}</option>)}
      </select>
    </label>
  )
}
