import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { CheckCircle2, X, XCircle } from 'lucide-react'

interface Aviso {
  id: number
  tipo: 'sucesso' | 'erro'
  texto: string
}

interface ValorAvisos {
  sucesso: (texto: string) => void
  erro: (texto: string) => void
}

const AvisosContext = createContext<ValorAvisos | null>(null)

/** Notificações temporárias (canto inferior direito). Erros ficam até serem fechados. */
export function AvisosProvider({ children }: { children: ReactNode }) {
  const [avisos, setAvisos] = useState<Aviso[]>([])

  const remover = useCallback((id: number) => setAvisos((a) => a.filter((x) => x.id !== id)), [])
  const adicionar = useCallback(
    (tipo: Aviso['tipo'], texto: string) => {
      const id = Date.now() + Math.random()
      setAvisos((a) => [...a.slice(-3), { id, tipo, texto }])
      if (tipo === 'sucesso') window.setTimeout(() => remover(id), 4000)
    },
    [remover],
  )

  const valor = useMemo<ValorAvisos>(
    () => ({ sucesso: (t) => adicionar('sucesso', t), erro: (t) => adicionar('erro', t) }),
    [adicionar],
  )

  return (
    <AvisosContext.Provider value={valor}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-[60] flex w-80 flex-col gap-2" aria-live="polite">
        {avisos.map((a) => (
          <div
            key={a.id}
            role={a.tipo === 'erro' ? 'alert' : 'status'}
            className={`pointer-events-auto flex items-start gap-2 rounded-lg border p-3 text-sm shadow-lg ${
              a.tipo === 'erro' ? 'border-red-200 bg-red-50 text-red-800' : 'border-green-200 bg-green-50 text-green-800'
            }`}
          >
            {a.tipo === 'erro' ? <XCircle className="h-5 w-5 shrink-0" aria-hidden /> : <CheckCircle2 className="h-5 w-5 shrink-0" aria-hidden />}
            <span className="flex-1">{a.texto}</span>
            <button type="button" onClick={() => remover(a.id)} aria-label="Fechar aviso" className="text-current opacity-60 hover:opacity-100">
              <X className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>
    </AvisosContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAvisos() {
  const ctx = useContext(AvisosContext)
  if (!ctx) throw new Error('useAvisos fora de AvisosProvider')
  return ctx
}
