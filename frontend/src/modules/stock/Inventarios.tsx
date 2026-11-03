import { useState } from 'react'
import { ClipboardCheck, Loader2, Plus } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Confirmacao, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Armazem, Inventario, InventarioResumo } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'
import { n3 } from './comum'

export function Inventarios({ armazens }: { armazens: Armazem[] }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const lista = useDados(() => api.get<InventarioResumo[]>('/api/stock/inventarios'), [])
  const [aberto, setAberto] = useState<number | null>(null)
  const [novo, setNovo] = useState(false)
  const activos = armazens.filter((a) => a.activo)
  const [armazemId, setArmazemId] = useState(activos.find((a) => a.principal)?.id ?? activos[0]?.id ?? 0)
  const [aCriar, setACriar] = useState(false)

  const criar = async () => {
    setACriar(true)
    try {
      const inv = await api.post<Inventario>('/api/stock/inventarios', { armazemId, observacoes: null })
      setNovo(false)
      await lista.recarregar()
      setAberto(inv.id)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setACriar(false)
    }
  }

  return (
    <div className="cartao">
      <div className="flex items-center justify-between border-b border-slate-200 p-4">
        <p className="text-sm text-slate-600">Contagem física: ao confirmar, o stock de cada artigo passa a ser a quantidade contada.</p>
        {tem('stock.ajustar') && (
          <button type="button" className="btn-primario" onClick={() => setNovo(true)} disabled={!podeGravar}>
            <Plus className="h-4 w-4" aria-hidden /> Novo inventário
          </button>
        )}
      </div>
      {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados?.length === 0 && <Vazio texto="Ainda não foram feitos inventários." />}
      {lista.dados && lista.dados.length > 0 && (
        <ul className="divide-y divide-slate-100">
          {lista.dados.map((i) => (
            <li key={i.id}>
              <button type="button" className="flex min-h-14 w-full items-center gap-3 px-4 text-left hover:bg-slate-50" onClick={() => setAberto(i.id)}>
                <ClipboardCheck className="h-5 w-5 text-slate-500" aria-hidden />
                <span className="flex-1">
                  <span className="font-medium">Inventário n.º {i.id} · {i.armazem}</span>
                  <span className="block text-xs text-slate-500">
                    Aberto em {formatarDataHora(i.criadoEmUtc)}{i.confirmadoEmUtc && ` · confirmado em ${formatarDataHora(i.confirmadoEmUtc)}`} · {i.linhas} artigos · {i.comDiferenca} com diferença
                  </span>
                </span>
                <Etiqueta cor={i.estado === 'Aberto' ? 'amarelo' : i.estado === 'Confirmado' ? 'verde' : 'cinza'}>{i.estado}</Etiqueta>
              </button>
            </li>
          ))}
        </ul>
      )}

      {novo && (
        <Modal titulo="Novo inventário" aoFechar={() => setNovo(false)}
          rodape={<>
            <button type="button" className="btn-secundario" onClick={() => setNovo(false)}>Cancelar</button>
            <button type="button" className="btn-primario" onClick={() => void criar()} disabled={aCriar || !podeGravar}>
              {aCriar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Abrir inventário
            </button>
          </>}>
          <label htmlFor="armInv" className="rotulo">Armazém</label>
          <select id="armInv" className="campo" value={armazemId} onChange={(e) => setArmazemId(Number(e.target.value))}>
            {activos.map((a) => <option key={a.id} value={a.id}>{a.nome}</option>)}
          </select>
          <p className="mt-2 text-sm text-slate-600">É criada uma folha com todos os artigos activos e o saldo actual. Só pode haver um inventário aberto por armazém.</p>
        </Modal>
      )}
      {aberto !== null && <DetalheInventario id={aberto} aoFechar={() => { setAberto(null); void lista.recarregar() }} />}
    </div>
  )
}

function DetalheInventario({ id, aoFechar }: { id: number; aoFechar: () => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const inv = useDados(() => api.get<Inventario>(`/api/stock/inventarios/${id}`), [id])
  const [contagens, setContagens] = useState<Record<number, string>>({})
  const [confirmar, setConfirmar] = useState<'confirmar' | 'cancelar' | null>(null)
  const [aGravar, setAGravar] = useState(false)
  const [filtro, setFiltro] = useState('')

  const d = inv.dados
  const editavel = d?.estado === 'Aberto' && tem('stock.ajustar')
  const valor = (linhaId: number, original: number) => (contagens[linhaId] ?? String(original))
  const alteradas = Object.entries(contagens).filter(([, v]) => v.trim() !== '')

  const guardar = async () => {
    if (!d) return null
    const linhas = alteradas.map(([linhaId, v]) => ({ linhaId: Number(linhaId), quantidadeContada: Number(v) }))
    if (linhas.some((l) => !(l.quantidadeContada >= 0))) {
      avisos.erro('Há quantidades inválidas (têm de ser zero ou positivas).')
      return null
    }
    const r = await api.put<Inventario>(`/api/stock/inventarios/${id}/contagem`, { linhas, observacoes: null, rowVersion: d.rowVersion })
    setContagens({})
    inv.setDados(r)
    return r
  }

  const executar = async (accao: 'guardar' | 'confirmar' | 'cancelar') => {
    setAGravar(true)
    try {
      let actual = d
      if (alteradas.length > 0 && accao !== 'cancelar') actual = await guardar()
      if (!actual) return
      if (accao === 'confirmar') {
        await api.post(`/api/stock/inventarios/${id}/confirmar`, { rowVersion: actual.rowVersion })
        avisos.sucesso('Inventário confirmado. O stock foi acertado.')
        aoFechar()
      } else if (accao === 'cancelar') {
        await api.post(`/api/stock/inventarios/${id}/cancelar`, { rowVersion: actual.rowVersion })
        avisos.sucesso('Inventário cancelado.')
        aoFechar()
      } else avisos.sucesso('Contagem guardada.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
      await inv.recarregar()
    } finally {
      setAGravar(false)
      setConfirmar(null)
    }
  }

  const linhas = d?.linhas.filter((l) => !filtro || l.artigo.toLowerCase().includes(filtro.toLowerCase()) || l.codigo.toLowerCase().includes(filtro.toLowerCase())) ?? []
  const comDiferenca = d?.linhas.filter((l) => Number(valor(l.id, l.quantidadeContada)) !== l.quantidadeSistema).length ?? 0

  return (
    <Modal titulo={d ? `Inventário n.º ${d.id} · ${d.armazem}` : 'Inventário'} aoFechar={aoFechar} largura="max-w-4xl"
      rodape={editavel ? <>
        <button type="button" className="btn-secundario text-red-700" onClick={() => setConfirmar('cancelar')} disabled={aGravar || !podeGravar}>Cancelar inventário</button>
        <button type="button" className="btn-secundario" onClick={() => void executar('guardar')} disabled={aGravar || !podeGravar || alteradas.length === 0}>Guardar contagem</button>
        <button type="button" className="btn-primario" onClick={() => setConfirmar('confirmar')} disabled={aGravar || !podeGravar}>
          {aGravar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Confirmar e acertar stock
        </button>
      </> : <button type="button" className="btn-primario" onClick={aoFechar}>Fechar</button>}>
      {inv.erro && <Alerta tipo="erro">{inv.erro}</Alerta>}
      {!d && !inv.erro && <Carregando />}
      {d && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-3 text-sm text-slate-600">
            <Etiqueta cor={d.estado === 'Aberto' ? 'amarelo' : d.estado === 'Confirmado' ? 'verde' : 'cinza'}>{d.estado}</Etiqueta>
            <span>Aberto por {d.criadoPor} em {formatarDataHora(d.criadoEmUtc)}</span>
            {d.confirmadoEmUtc && <span>· confirmado por {d.confirmadoPor} em {formatarDataHora(d.confirmadoEmUtc)}</span>}
            <span className="ml-auto">{comDiferenca} artigos com diferença</span>
          </div>
          {editavel && <p className="text-xs text-slate-500">Saldo do sistema no momento da abertura; ao confirmar, a diferença é calculada contra o saldo nesse momento.</p>}
          <input type="search" className="campo" placeholder="Filtrar artigos…" value={filtro} onChange={(e) => setFiltro(e.target.value)} aria-label="Filtrar artigos" />
          <div className="max-h-[50vh] overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-white text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Artigo</th>
                  <th scope="col" className="py-2 text-right">Sistema</th>
                  <th scope="col" className="py-2 text-right">Contado</th>
                  <th scope="col" className="py-2 text-right">Diferença</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {linhas.map((l) => {
                  const contado = Number(valor(l.id, l.quantidadeContada))
                  const dif = Number.isFinite(contado) ? contado - l.quantidadeSistema : NaN
                  return (
                    <tr key={l.id}>
                      <td className="py-1.5"><span className="font-medium">{l.artigo}</span> <span className="text-xs text-slate-500">{l.codigo}</span></td>
                      <td className="py-1.5 text-right">{n3.format(l.quantidadeSistema)} {l.unidade}</td>
                      <td className="py-1.5 pl-4 text-right">
                        {editavel ? (
                          <input type="number" min={0} step="0.001" className="campo ml-auto w-32 text-right" aria-label={`Contado ${l.artigo}`}
                            value={valor(l.id, l.quantidadeContada)} onChange={(e) => setContagens((c) => ({ ...c, [l.id]: e.target.value }))} />
                        ) : `${n3.format(l.quantidadeContada)} ${l.unidade}`}
                      </td>
                      <td className={`py-1.5 text-right font-medium ${dif < 0 ? 'text-red-700' : dif > 0 ? 'text-green-700' : 'text-slate-400'}`}>
                        {Number.isFinite(dif) ? `${dif > 0 ? '+' : ''}${n3.format(dif)}` : '—'}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {confirmar && (
        <Confirmacao
          titulo={confirmar === 'confirmar' ? 'Confirmar inventário' : 'Cancelar inventário'}
          mensagem={confirmar === 'confirmar'
            ? <>O stock de {comDiferenca} artigo(s) vai ser acertado para as quantidades contadas. Esta operação não pode ser desfeita.</>
            : <>A contagem é descartada e o stock não é alterado.</>}
          textoConfirmar={confirmar === 'confirmar' ? 'Confirmar' : 'Cancelar inventário'}
          perigo={confirmar === 'cancelar'}
          aProcessar={aGravar}
          aoCancelar={() => setConfirmar(null)}
          aoConfirmar={() => void executar(confirmar)}
        />
      )}
    </Modal>
  )
}
