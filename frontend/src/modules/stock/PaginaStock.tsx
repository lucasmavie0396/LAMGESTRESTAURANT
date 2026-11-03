import { useState } from 'react'
import { Loader2, Plus } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Armazem, Unidade } from '../../types/api'
import { Artigos, ListaMovimentos } from './Artigos'
import { FichasTecnicas } from './FichasTecnicas'
import { Inventarios } from './Inventarios'

type Separador = 'artigos' | 'movimentos' | 'inventarios' | 'fichas'

export function PaginaStock() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const unidades = useDados(() => api.get<Unidade[]>('/api/stock/unidades'), [])
  const armazens = useDados(() => api.get<Armazem[]>('/api/stock/armazens'), [])
  const separadores = ([
    ['artigos', 'Artigos', tem('stock.ver')],
    ['movimentos', 'Movimentos', tem('stock.ver')],
    ['inventarios', 'Inventários', tem('stock.ver')],
    ['fichas', 'Fichas técnicas', tem('receitas.gerir')],
  ] as const).filter(([, , visivel]) => visivel)
  const [separador, setSeparador] = useState<Separador>(separadores[0]?.[0] ?? 'artigos')

  const [novo, setNovo] = useState<string | null>(null)
  const [aCriar, setACriar] = useState(false)
  const criarArmazem = async () => {
    if (!novo?.trim()) return
    setACriar(true)
    try {
      await api.post('/api/stock/armazens', { nome: novo.trim() })
      avisos.sucesso('Armazém criado.')
      setNovo(null)
      await armazens.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setACriar(false)
    }
  }

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Stock</h1>
        {tem('stock.gerir') && (
          <div className="flex items-center gap-2 text-sm text-slate-600">
            Armazéns: {armazens.dados?.filter((a) => a.activo).map((a) => a.nome).join(', ')}
            <button type="button" className="btn-secundario min-h-9 px-3" onClick={() => setNovo('')} disabled={!podeGravar}>
              <Plus className="h-4 w-4" aria-hidden /> Armazém
            </button>
          </div>
        )}
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
        </>
      )}
      {novo !== null && (
        <Modal titulo="Novo armazém" aoFechar={() => setNovo(null)}
          rodape={<>
            <button type="button" className="btn-secundario" onClick={() => setNovo(null)}>Cancelar</button>
            <button type="button" className="btn-primario" onClick={() => void criarArmazem()} disabled={aCriar || !podeGravar || !novo.trim()}>
              {aCriar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Criar
            </button>
          </>}>
          <label htmlFor="nomeArmazem" className="rotulo">Nome</label>
          <input id="nomeArmazem" className="campo" maxLength={200} value={novo} onChange={(e) => setNovo(e.target.value)} placeholder="Ex.: Bar, Câmara frigorífica" />
        </Modal>
      )}
    </div>
  )
}
