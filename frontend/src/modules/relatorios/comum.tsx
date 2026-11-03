import type { ReactNode } from 'react'
import { FileSpreadsheet, FileText, Info } from 'lucide-react'
import { descarregarCsv } from '../../utils/csv'
import { descarregarXlsx } from '../../utils/xlsx'
import type { Tabela } from './formato'

/** Botões CSV e Excel; os dados só são calculados ao carregar (podem vir de um pedido ao servidor). */
export function BotoesExportar({ titulo, exportar }: { titulo: string; exportar: () => Tabela | Promise<Tabela> }) {
  const executar = async (formato: 'csv' | 'xlsx') => {
    const t = await exportar()
    if (formato === 'csv') descarregarCsv(t.nome, t.cabecalho, t.linhas)
    else descarregarXlsx(t.nome, t.cabecalho, t.linhas, titulo)
  }
  return (
    <div className="flex gap-1 print:hidden">
      <button type="button" className="btn-icone h-9 w-9" onClick={() => void executar('csv')} title="Exportar CSV" aria-label={`Exportar ${titulo} em CSV`}>
        <FileText className="h-4 w-4" />
      </button>
      <button type="button" className="btn-icone h-9 w-9" onClick={() => void executar('xlsx')} title="Exportar Excel (.xlsx)" aria-label={`Exportar ${titulo} em Excel`}>
        <FileSpreadsheet className="h-4 w-4 text-green-700" />
      </button>
    </div>
  )
}

export function Seccao({ titulo, children, exportar, accoes }: {
  titulo: string
  children: ReactNode
  exportar?: () => Tabela | Promise<Tabela>
  /** Controlos extra no cabeçalho (ex.: ordenação). */
  accoes?: ReactNode
}) {
  return (
    <section className="cartao break-inside-avoid p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{titulo}</h2>
        <div className="flex items-center gap-2">
          {accoes}
          {exportar && <BotoesExportar titulo={titulo} exportar={exportar} />}
        </div>
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
