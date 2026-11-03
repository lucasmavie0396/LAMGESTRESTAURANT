import type { ReactNode } from 'react'
import { Download, Info } from 'lucide-react'

export function Seccao({ titulo, children, csv }: { titulo: string; children: ReactNode; csv?: () => void }) {
  return (
    <section className="cartao break-inside-avoid p-4">
      <div className="mb-3 flex items-center justify-between gap-2">
        <h2 className="font-semibold">{titulo}</h2>
        {csv && (
          <button type="button" className="btn-icone h-9 w-9 print:hidden" onClick={csv} title="Exportar CSV (Excel)" aria-label={`Exportar ${titulo} em CSV`}>
            <Download className="h-4 w-4" />
          </button>
        )}
      </div>
      {children}
    </section>
  )
}

/** Fila de indicadores principais: [rótulo, valor, nota]. */
export function Indicadores({ itens }: { itens: [string, string, string][] }) {
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {itens.map(([rotulo, valor, nota]) => (
        <div key={rotulo} className="cartao p-4">
          <p className="text-sm text-slate-500">{rotulo}</p>
          <p className="mt-1 text-2xl font-semibold tabular-nums">{valor}</p>
          <p className="mt-1 text-xs text-slate-500">{nota}</p>
        </div>
      ))}
    </div>
  )
}

export function Metodologia({ children }: { children: ReactNode }) {
  return (
    <div className="flex gap-2 rounded-lg bg-slate-50 p-3 text-xs text-slate-600">
      <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      <p>{children}</p>
    </div>
  )
}
