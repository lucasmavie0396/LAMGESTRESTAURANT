import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { formatarMoeda } from '../utils/formatar'

// Gráficos de série única: uma só cor (azul principal), sem legenda — o título identifica a série.
// Grelha e eixos recessivos; tooltip ao passar o rato/tocar; valores sempre também disponíveis em tabela.

const AZUL = '#2563eb'
const TINTA_SECUNDARIA = '#64748b'
const GRELHA = '#e2e8f0'

const compacto = new Intl.NumberFormat('pt-MZ', { notation: 'compact', maximumFractionDigits: 1 })

export interface PontoBarra {
  rotulo: string
  valor: number
  detalhe?: string
}

export function GraficoBarras({ dados, altura = 240, titulo }: { dados: PontoBarra[]; altura?: number; titulo: string }) {
  return (
    <figure aria-label={titulo}>
      <ResponsiveContainer width="100%" height={altura}>
        <BarChart data={dados} margin={{ top: 8, right: 8, bottom: 0, left: 0 }} barCategoryGap="28%">
          <CartesianGrid vertical={false} stroke={GRELHA} />
          <XAxis dataKey="rotulo" tickLine={false} axisLine={{ stroke: GRELHA }} tick={{ fill: TINTA_SECUNDARIA, fontSize: 12 }} />
          <YAxis tickLine={false} axisLine={false} width={48} tick={{ fill: TINTA_SECUNDARIA, fontSize: 12 }} tickFormatter={(v: number) => compacto.format(v)} />
          <Tooltip
            cursor={{ fill: '#eff6ff' }}
            content={({ active, payload }) => {
              if (!active || !payload?.length) return null
              const p = payload[0].payload as PontoBarra
              return (
                <div className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm shadow-md">
                  <p className="font-medium text-slate-800">{p.rotulo}</p>
                  <p className="tabular-nums text-slate-700">{formatarMoeda(p.valor)}</p>
                  {p.detalhe && <p className="text-xs text-slate-500">{p.detalhe}</p>}
                </div>
              )
            }}
          />
          <Bar dataKey="valor" fill={AZUL} radius={[4, 4, 0, 0]} maxBarSize={48} isAnimationActive={false} />
        </BarChart>
      </ResponsiveContainer>
    </figure>
  )
}

/** Lista com barra proporcional: comparação de partes sem gráfico circular. */
export function BarrasHorizontais({ linhas, vazio = 'Sem dados.' }: { linhas: { rotulo: string; valor: number; extra?: string }[]; vazio?: string }) {
  const maximo = Math.max(...linhas.map((l) => l.valor), 0)
  if (linhas.length === 0) return <p className="py-4 text-sm text-slate-500">{vazio}</p>
  return (
    <ul className="space-y-2">
      {linhas.map((l) => (
        <li key={l.rotulo}>
          <div className="flex justify-between gap-2 text-sm">
            <span className="truncate text-slate-700">{l.rotulo}</span>
            <span className="shrink-0 font-medium tabular-nums text-slate-800">
              {formatarMoeda(l.valor)}
              {l.extra && <span className="ml-1 text-xs font-normal text-slate-500">{l.extra}</span>}
            </span>
          </div>
          <div className="mt-1 h-2 rounded-full bg-slate-100" aria-hidden>
            <div className="h-2 rounded-full bg-primaria-600" style={{ width: `${maximo ? Math.max(2, (l.valor / maximo) * 100) : 0}%` }} />
          </div>
        </li>
      ))}
    </ul>
  )
}
