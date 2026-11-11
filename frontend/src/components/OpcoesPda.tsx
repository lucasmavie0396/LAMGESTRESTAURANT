import { useState } from 'react'
import { Settings } from 'lucide-react'
import { pda, type DestinoImpressao } from '../utils/pda'

/** Na app de venda: onde saem o recibo e a conta (impressora do posto ou do PDA) e acesso à ligação ao servidor. */
export function OpcoesPda() {
  const ponte = pda()
  const [destino, setDestino] = useState<DestinoImpressao>(ponte?.destino() === 'pda' ? 'pda' : 'posto')
  if (!ponte) return null
  const temImpressora = ponte.temImpressora()

  const escolher = (d: DestinoImpressao) => {
    ponte.definirDestino(d)
    setDestino(d)
  }

  return (
    <div className="space-y-2 border-t border-primaria-800 p-3 text-sm">
      <p className="text-xs text-primaria-200">Ligado ao posto <span className="font-semibold text-white">{ponte.posto()}</span></p>
      <fieldset>
        <legend className="mb-1 text-xs font-semibold uppercase tracking-wide text-primaria-300">Recibos e contas</legend>
        <div className="grid grid-cols-2 gap-1">
          {([['posto', 'Posto'], ['pda', 'Este PDA']] as const).map(([v, t]) => (
            <button key={v} type="button" aria-pressed={destino === v} disabled={v === 'pda' && !temImpressora} onClick={() => escolher(v)}
              className={`min-h-11 rounded-lg px-2 font-medium disabled:opacity-40 ${destino === v ? 'bg-primaria-600 text-white' : 'bg-primaria-800'}`}>{t}</button>
          ))}
        </div>
        {!temImpressora && <p className="mt-1 text-xs text-primaria-200">Impressora do PDA não detectada.</p>}
      </fieldset>
      <button type="button" className="flex min-h-11 w-full items-center gap-2 rounded-lg px-2 hover:bg-primaria-800" onClick={() => ponte.configurar()}>
        <Settings className="h-4 w-4" aria-hidden /> Ligação ao servidor
      </button>
    </div>
  )
}
