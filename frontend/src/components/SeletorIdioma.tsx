import { Languages } from 'lucide-react'
import { definirIdioma, idiomaActual, NOMES_IDIOMA, type Idioma } from '../i18n/idioma'

/** Troca Português / English neste computador (a página recarrega no idioma escolhido). */
export function SeletorIdioma({ variante = 'barra' }: { variante?: 'barra' | 'entrada' }) {
  const entrada = variante === 'entrada'
  return (
    <div translate="no" role="group" aria-label="Idioma / Language"
      className={`inline-flex items-center gap-1 rounded-lg p-0.5 text-xs font-semibold ${entrada ? 'bg-white/15 text-white' : 'border border-slate-300 text-slate-600'}`}>
      <Languages className={`mx-1 h-4 w-4 ${entrada ? '' : 'text-slate-500'}`} aria-hidden />
      {(Object.keys(NOMES_IDIOMA) as Idioma[]).map((i) => {
        const activo = i === idiomaActual
        return (
          <button key={i} type="button" title={NOMES_IDIOMA[i]} aria-pressed={activo} onClick={() => definirIdioma(i)}
            className={`rounded-md px-2 py-1 uppercase ${activo ? (entrada ? 'bg-white text-primaria-800' : 'bg-primaria-600 text-white') : entrada ? 'hover:bg-white/20' : 'hover:bg-slate-100'}`}>
            {i}
          </button>
        )
      })}
    </div>
  )
}
