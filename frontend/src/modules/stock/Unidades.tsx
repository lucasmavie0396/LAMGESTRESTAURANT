import { useState } from 'react'
import { Loader2, Pencil, Plus, Power } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Dimensao, Unidade } from '../../types/api'
import { n3 } from './comum'

const DIMENSOES: { valor: Dimensao; rotulo: string; base: string }[] = [
  { valor: 'Unidade', rotulo: 'Contagem', base: 'un' },
  { valor: 'Massa', rotulo: 'Peso', base: 'kg' },
  { valor: 'Volume', rotulo: 'Volume', base: 'l' },
]

/** Tabela de unidades: as que se escolhem nos produtos, artigos e fichas técnicas. */
export function Unidades({ aoAlterar }: { aoAlterar: () => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const lista = useDados(() => api.get<Unidade[]>('/api/stock/unidades?incluirInactivas=true'), [])
  const [editar, setEditar] = useState<Unidade | 'nova' | null>(null)
  const podeGerir = tem('stock.gerir')

  const alternar = async (u: Unidade) => {
    try {
      await api.post(`/api/stock/unidades/${u.id}/activo`, { activo: !u.activo })
      avisos.sucesso(u.activo ? 'Unidade desactivada.' : 'Unidade activada.')
      await lista.recarregar()
      aoAlterar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  if (lista.erro) return <Alerta tipo="erro">{lista.erro}</Alerta>
  if (!lista.dados) return <Carregando />

  return (
    <section className="cartao p-4">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-semibold">Unidades</h2>
          <p className="max-w-3xl text-sm text-slate-600">Unidades de venda e de stock. O factor converte entre unidades do mesmo tipo (ex.: 1 g = 0,001 kg; 1 caixa = 12 un).</p>
        </div>
        {podeGerir && (
          <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setEditar('nova')}>
            <Plus className="h-4 w-4" aria-hidden /> Nova unidade
          </button>
        )}
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-slate-600">
            <tr className="border-b border-slate-200">
              <th scope="col" className="py-2">Código</th>
              <th scope="col" className="py-2">Nome</th>
              <th scope="col" className="py-2">Tipo</th>
              <th scope="col" className="py-2">Conversão</th>
              {podeGerir && <th scope="col" className="py-2 text-right">Acções</th>}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {lista.dados.map((u) => {
              const d = DIMENSOES.find((x) => x.valor === u.dimensao)
              return (
                <tr key={u.id} className={u.activo ? '' : 'opacity-60'}>
                  <td className="py-2 font-medium">{u.codigo}{!u.activo && <span className="ml-2"><Etiqueta cor="cinza">Inactiva</Etiqueta></span>}</td>
                  <td className="py-2">{u.nome}</td>
                  <td className="py-2">{d?.rotulo}</td>
                  <td className="py-2 tabular-nums">1 {u.codigo} = {n3.format(u.factorParaBase)} {d?.base}</td>
                  {podeGerir && (
                    <td className="py-2">
                      <div className="flex justify-end gap-1">
                        <button type="button" className="btn-icone" aria-label={`Editar ${u.codigo}`} disabled={!podeGravar} onClick={() => setEditar(u)}><Pencil className="h-4 w-4" /></button>
                        <button type="button" className="btn-icone" aria-label={`${u.activo ? 'Desactivar' : 'Activar'} ${u.codigo}`} disabled={!podeGravar} onClick={() => void alternar(u)}>
                          <Power className={`h-4 w-4 ${u.activo ? 'text-red-600' : 'text-green-700'}`} />
                        </button>
                      </div>
                    </td>
                  )}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {editar && <FormUnidade unidade={editar === 'nova' ? null : editar} aoFechar={(gravou) => { setEditar(null); if (gravou) { void lista.recarregar(); aoAlterar() } }} />}
    </section>
  )
}

function FormUnidade({ unidade, aoFechar }: { unidade: Unidade | null; aoFechar: (gravou: boolean) => void }) {
  const avisos = useAvisos()
  const [codigo, setCodigo] = useState(unidade?.codigo ?? '')
  const [nome, setNome] = useState(unidade?.nome ?? '')
  const [dimensao, setDimensao] = useState<Dimensao>(unidade?.dimensao ?? 'Unidade')
  const [factor, setFactor] = useState(unidade ? String(unidade.factorParaBase).replace('.', ',') : '1')
  const [aGuardar, setAGuardar] = useState(false)
  const valorFactor = Number(factor.replace(',', '.'))
  const valido = !!codigo.trim() && !!nome.trim() && valorFactor > 0
  const base = DIMENSOES.find((d) => d.valor === dimensao)?.base

  const guardar = async () => {
    setAGuardar(true)
    try {
      const corpo = { codigo: codigo.trim(), nome: nome.trim(), dimensao, factorParaBase: valorFactor }
      if (unidade) await api.put(`/api/stock/unidades/${unidade.id}`, corpo)
      else await api.post('/api/stock/unidades', corpo)
      avisos.sucesso(unidade ? 'Unidade actualizada.' : 'Unidade criada.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={unidade ? `Editar ${unidade.codigo}` : 'Nova unidade'} aoFechar={() => aoFechar(false)}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar(false)}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aGuardar || !valido} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="grid gap-3 sm:grid-cols-2">
        <div>
          <label htmlFor="uniCodigo" className="rotulo">Código</label>
          <input id="uniCodigo" className="campo" maxLength={10} value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="Ex.: cx" />
        </div>
        <div>
          <label htmlFor="uniNome" className="rotulo">Nome</label>
          <input id="uniNome" className="campo" maxLength={100} value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: Caixa" />
        </div>
        <div>
          <label htmlFor="uniTipo" className="rotulo">Tipo</label>
          <select id="uniTipo" className="campo" value={dimensao} onChange={(e) => setDimensao(e.target.value as Dimensao)}>
            {DIMENSOES.map((d) => <option key={d.valor} value={d.valor}>{d.rotulo}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="uniFactor" className="rotulo">1 {codigo.trim() || 'unidade'} equivale a ({base})</label>
          <input id="uniFactor" className="campo" inputMode="decimal" value={factor} onChange={(e) => setFactor(e.target.value)} aria-invalid={!(valorFactor > 0)} />
        </div>
      </div>
    </Modal>
  )
}
