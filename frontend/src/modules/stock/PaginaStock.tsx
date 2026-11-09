import { useState } from 'react'
import { api } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useSessao } from '../../contexts/SessaoContext'
import { useDados } from '../../hooks/useDados'
import type { Armazem, Unidade } from '../../types/api'
import { Armazens } from './Armazens'
import { Artigos, ListaMovimentos } from './Artigos'
import { Unidades } from './Unidades'
import { FichasTecnicas } from './FichasTecnicas'
import { Inventarios } from './Inventarios'

type Separador = 'artigos' | 'movimentos' | 'inventarios' | 'fichas'

export function PaginaStock() {
  const { tem } = useSessao()
  const unidades = useDados(() => api.get<Unidade[]>('/api/stock/unidades'), [])
  const armazens = useDados(() => api.get<Armazem[]>('/api/stock/armazens'), [])
  const separadores = ([
    ['artigos', 'Artigos', tem('stock.ver')],
    ['movimentos', 'Movimentos', tem('stock.ver')],
    ['inventarios', 'Inventários', tem('stock.ver')],
    ['armazens', 'Armazéns', tem('stock.ver')],
    ['unidades', 'Unidades', tem('stock.ver')],
    ['fichas', 'Fichas técnicas', tem('receitas.gerir')],
  ] as const).filter(([, , visivel]) => visivel)
  const [separador, setSeparador] = useState<Separador>(separadores[0]?.[0] ?? 'artigos')

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Stock</h1>
      </div>
      <div className="flex gap-1 overflow-x-auto border-b border-slate-200" role="tablist">
        {separadores.map(([s, rotulo]) => (
          <button key={s} type="button" role="tab" aria-selected={separador === s} onClick={() => setSeparador(s)}
            className={`min-h-11 shrink-0 border-b-2 px-4 text-sm font-medium ${separador === s ? 'border-primaria-600 text-primaria-700' : 'border-transparent text-slate-600 hover:text-slate-900'}`}>
            {rotulo}
          </button>
        ))}
      </div>
      {(unidades.erro || armazens.erro) && <Alerta tipo="erro">{unidades.erro ?? armazens.erro}</Alerta>}
      {(!unidades.dados || !armazens.dados) && !unidades.erro && !armazens.erro && <Carregando />}
      {unidades.dados && armazens.dados && (
        <>
          {separador === 'artigos' && <Artigos unidades={unidades.dados} armazens={armazens.dados} />}
          {separador === 'movimentos' && <div className="cartao"><ListaMovimentos /></div>}
          {separador === 'inventarios' && <Inventarios armazens={armazens.dados} />}
          {separador === 'fichas' && <FichasTecnicas unidades={unidades.dados} />}
          {separador === 'armazens' && <Armazens armazens={armazens.dados} aoAlterar={() => void armazens.recarregar()} />}
          {separador === 'unidades' && <Unidades aoAlterar={() => void unidades.recarregar()} />}
        </>
      )}
    </div>
  )
}
