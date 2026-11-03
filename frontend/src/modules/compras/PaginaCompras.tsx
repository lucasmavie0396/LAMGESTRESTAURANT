import { useMemo, useState } from 'react'
import { CheckCircle2, Loader2, Pencil, Plus, Power, Search, Trash2 } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Confirmacao, Etiqueta, Modal, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Armazem, Artigo, Compra, CompraResumo, EstadoCompra, Fornecedor, Pagina, Unidade } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { DialogoMotivo } from '../pos/Dialogos'
import { n3 } from '../stock/comum'

const COR_ESTADO: Record<EstadoCompra, 'amarelo' | 'verde' | 'cinza'> = { Rascunho: 'amarelo', Confirmada: 'verde', Anulada: 'cinza' }
const dataLocal = (iso: string) => new Intl.DateTimeFormat('pt-MZ', { timeZone: 'UTC' }).format(new Date(`${iso}T00:00:00Z`))
const hojeIso = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Maputo' }).format(new Date())

export function PaginaCompras() {
  const { tem } = useSessao()
  const [separador, setSeparador] = useState<'compras' | 'fornecedores'>('compras')
  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <h1 className="text-2xl font-semibold">Compras</h1>
      <div className="flex gap-1 border-b border-slate-200" role="tablist">
        {(['compras', 'fornecedores'] as const).map((s) => (
          <button key={s} type="button" role="tab" aria-selected={separador === s} onClick={() => setSeparador(s)}
            className={`min-h-11 border-b-2 px-4 text-sm font-medium ${separador === s ? 'border-primaria-600 text-primaria-700' : 'border-transparent text-slate-600 hover:text-slate-900'}`}>
            {s === 'compras' ? 'Compras' : 'Fornecedores'}
          </button>
        ))}
      </div>
      {separador === 'compras' ? <ListaCompras /> : <Fornecedores podeGerir={tem('compras.gerir')} />}
    </div>
  )
}

function ListaCompras() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const [estado, setEstado] = useState<'' | EstadoCompra>('')
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const [pagina, setPagina] = useState(1)
  const [aberta, setAberta] = useState<number | 'nova' | null>(null)
  const lista = useDados(() => api.get<Pagina<CompraResumo>>('/api/compras' + query({ estado, pesquisa: pesquisaAtrasada, pagina, tamanho: 25 })), [estado, pesquisaAtrasada, pagina])

  return (
    <div className="cartao">
      <div className="flex flex-wrap items-center gap-3 border-b border-slate-200 p-4">
        <div className="relative min-w-60 flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input type="search" className="campo pl-9" placeholder="N.º do documento ou fornecedor…" value={pesquisa}
            onChange={(e) => { setPesquisa(e.target.value); setPagina(1) }} aria-label="Pesquisar compras" />
        </div>
        <select className="campo w-44" value={estado} onChange={(e) => { setEstado(e.target.value as typeof estado); setPagina(1) }} aria-label="Estado">
          <option value="">Todos os estados</option>
          <option value="Rascunho">Rascunhos</option>
          <option value="Confirmada">Confirmadas</option>
          <option value="Anulada">Anuladas</option>
        </select>
        {tem('compras.gerir') && (
          <button type="button" className="btn-primario" onClick={() => setAberta('nova')} disabled={!podeGravar}>
            <Plus className="h-4 w-4" aria-hidden /> Nova compra
          </button>
        )}
      </div>
      {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados?.itens.length === 0 && <Vazio texto="Sem compras registadas." />}
      {lista.dados && lista.dados.itens.length > 0 && (
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-slate-600">
            <tr>
              <th scope="col" className="px-4 py-3">Data</th>
              <th scope="col" className="px-4 py-3">Fornecedor</th>
              <th scope="col" className="px-4 py-3">Documento</th>
              <th scope="col" className="px-4 py-3 text-right">Linhas</th>
              <th scope="col" className="px-4 py-3 text-right">Total</th>
              <th scope="col" className="px-4 py-3">Estado</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 tabular-nums">
            {lista.dados.itens.map((c) => (
              <tr key={c.id} className="cursor-pointer hover:bg-slate-50" onClick={() => setAberta(c.id)}>
                <td className="px-4 py-2">{dataLocal(c.dataDocumento)}</td>
                <td className="px-4 py-2 font-medium">{c.fornecedor}</td>
                <td className="px-4 py-2">{c.numeroDocumento}</td>
                <td className="px-4 py-2 text-right">{c.numeroItens}</td>
                <td className="px-4 py-2 text-right">{formatarMoeda(c.total)}</td>
                <td className="px-4 py-2"><Etiqueta cor={COR_ESTADO[c.estado]}>{c.estado}</Etiqueta></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {lista.dados && lista.dados.total > 0 && <Paginacao pagina={pagina} tamanho={25} total={lista.dados.total} aoMudar={setPagina} />}
      {aberta !== null && <DetalheCompra id={aberta === 'nova' ? null : aberta} aoFechar={() => { setAberta(null); void lista.recarregar() }} />}
    </div>
  )
}

interface LinhaForm { artigoId: number; quantidade: string; unidadeId: number; custo: string; desconto: string; iva: string }

/** Detalhe/edição de uma compra. Rascunhos editam-se; confirmadas só se consultam ou anulam. */
function DetalheCompra({ id, aoFechar }: { id: number | null; aoFechar: () => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const compra = useDados(() => (id ? api.get<Compra>(`/api/compras/${id}`) : Promise.resolve(null)), [id])
  const fornecedores = useDados(() => api.get<Fornecedor[]>('/api/fornecedores'), [])
  const armazens = useDados(() => api.get<Armazem[]>('/api/stock/armazens'), [])
  const artigos = useDados(() => api.get<Pagina<Artigo>>('/api/stock/artigos?activo=true&tamanho=200'), [])
  const unidades = useDados(() => api.get<Unidade[]>('/api/stock/unidades'), [])
  const [editar, setEditar] = useState(id === null)
  const [dialogo, setDialogo] = useState<'confirmar' | 'eliminar' | 'anular' | null>(null)
  const [aGravar, setAGravar] = useState(false)

  const c = compra.dados
  const carregado = fornecedores.dados && armazens.dados && artigos.dados && unidades.dados && (id === null || c)

  const executar = async (accao: () => Promise<unknown>, sucesso: string, fechar = false) => {
    setAGravar(true)
    try {
      await accao()
      avisos.sucesso(sucesso)
      setDialogo(null)
      if (fechar) aoFechar()
      else await compra.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
      setDialogo(null)
    } finally {
      setAGravar(false)
    }
  }

  return (
    <Modal titulo={id === null ? 'Nova compra' : `Compra ${c?.numeroDocumento ?? ''}`} aoFechar={aoFechar} largura="max-w-5xl"
      rodape={!editar && c ? <>
        {c.estado === 'Rascunho' && tem('compras.gerir') && <>
          <button type="button" className="btn-secundario mr-auto text-red-700" onClick={() => setDialogo('eliminar')} disabled={!podeGravar}><Trash2 className="h-4 w-4" aria-hidden /> Eliminar rascunho</button>
          <button type="button" className="btn-secundario" onClick={() => setEditar(true)} disabled={!podeGravar}><Pencil className="h-4 w-4" aria-hidden /> Editar</button>
          <button type="button" className="btn-primario" onClick={() => setDialogo('confirmar')} disabled={!podeGravar}><CheckCircle2 className="h-4 w-4" aria-hidden /> Confirmar e dar entrada no stock</button>
        </>}
        {c.estado === 'Confirmada' && tem('compras.anular') && (
          <button type="button" className="btn-secundario mr-auto text-red-700" onClick={() => setDialogo('anular')} disabled={!podeGravar}>Anular compra</button>
        )}
        <button type="button" className="btn-secundario" onClick={aoFechar}>Fechar</button>
      </> : undefined}>
      {(compra.erro || fornecedores.erro || artigos.erro) && <Alerta tipo="erro">{compra.erro ?? fornecedores.erro ?? artigos.erro}</Alerta>}
      {!carregado && <Carregando />}
      {carregado && editar && (
        <FormCompra compra={c ?? undefined} fornecedores={fornecedores.dados!.filter((f) => f.activo)} armazens={armazens.dados!.filter((a) => a.activo)}
          artigos={artigos.dados!.itens} unidades={unidades.dados!}
          aoCancelar={() => (id === null ? aoFechar() : setEditar(false))}
          aoGuardar={async () => { if (id === null) aoFechar(); else { setEditar(false); await compra.recarregar() } }} />
      )}
      {carregado && !editar && c && (
        <div className="space-y-4">
          <div className="grid gap-2 text-sm sm:grid-cols-3">
            <p><span className="text-slate-500">Fornecedor:</span> <strong>{c.fornecedor}</strong></p>
            <p><span className="text-slate-500">Documento:</span> {c.numeroDocumento} de {dataLocal(c.dataDocumento)}{c.dataVencimento && ` (vence ${dataLocal(c.dataVencimento)})`}</p>
            <p><span className="text-slate-500">Armazém:</span> {c.armazem} · <Etiqueta cor={COR_ESTADO[c.estado]}>{c.estado}</Etiqueta></p>
          </div>
          <table className="w-full text-left text-sm">
            <thead className="text-slate-600">
              <tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Artigo</th>
                <th scope="col" className="py-2 text-right">Qtd.</th>
                <th scope="col" className="py-2 text-right">Custo unit. (s/ IVA)</th>
                <th scope="col" className="py-2 text-right">Desconto</th>
                <th scope="col" className="py-2 text-right">IVA</th>
                <th scope="col" className="py-2 text-right">Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {c.itens.map((i) => (
                <tr key={i.id}>
                  <td className="py-1.5">{i.artigo}</td>
                  <td className="py-1.5 text-right">{n3.format(i.quantidade)} {i.unidade}</td>
                  <td className="py-1.5 text-right">{formatarMoeda(i.custoUnitario)}</td>
                  <td className="py-1.5 text-right">{formatarMoeda(i.desconto)}</td>
                  <td className="py-1.5 text-right">{formatarMoeda(i.valorImposto)} <span className="text-xs text-slate-500">({n3.format(i.taxaImpostoPercentagem)}%)</span></td>
                  <td className="py-1.5 text-right font-medium">{formatarMoeda(i.total)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Totais subtotal={c.subtotal} desconto={c.desconto} impostos={c.impostos} total={c.total} />
          <p className="text-xs text-slate-500">
            Registada por {c.criadoPor}.{c.confirmadaEmUtc && ` Confirmada por ${c.confirmadaPor} em ${formatarDataHora(c.confirmadaEmUtc)}.`}
            {c.anuladaEmUtc && ` Anulada em ${formatarDataHora(c.anuladaEmUtc)}: ${c.motivoAnulacao}`}
          </p>
          {c.observacoes && <p className="text-sm text-slate-600">{c.observacoes}</p>}
        </div>
      )}
      {dialogo === 'confirmar' && c && (
        <Confirmacao titulo="Confirmar compra" textoConfirmar="Confirmar" aProcessar={aGravar}
          mensagem={<>As {c.itens.length} linhas entram no stock de <strong>{c.armazem}</strong> e o custo médio dos artigos é actualizado. Depois de confirmada, a compra só pode ser anulada.</>}
          aoCancelar={() => setDialogo(null)} aoConfirmar={() => void executar(() => api.post(`/api/compras/${c.id}/confirmar`, { rowVersion: c.rowVersion }), 'Compra confirmada. Stock actualizado.')} />
      )}
      {dialogo === 'eliminar' && c && (
        <Confirmacao titulo="Eliminar rascunho" textoConfirmar="Eliminar" perigo aProcessar={aGravar}
          mensagem="O rascunho é eliminado. Não afecta o stock."
          aoCancelar={() => setDialogo(null)} aoConfirmar={() => void executar(() => api.post(`/api/compras/${c.id}/eliminar`, { rowVersion: c.rowVersion }), 'Rascunho eliminado.', true)} />
      )}
      {dialogo === 'anular' && c && (
        <DialogoMotivo titulo={`Anular compra ${c.numeroDocumento}`} texto="Anular compra" aoFechar={() => setDialogo(null)}
          descricao="O stock que entrou com esta compra é retirado (só é possível se ainda não tiver sido consumido). Fica registado na auditoria."
          aoConfirmar={(motivo) => executar(() => api.post(`/api/compras/${c.id}/anular`, { motivo, rowVersion: c.rowVersion }), 'Compra anulada.')} />
      )}
    </Modal>
  )
}

function Totais({ subtotal, desconto, impostos, total, estimativa }: { subtotal: number; desconto: number; impostos: number; total: number; estimativa?: boolean }) {
  return (
    <dl className="ml-auto w-full max-w-xs space-y-1 text-sm tabular-nums">
      <div className="flex justify-between"><dt>Subtotal</dt><dd>{formatarMoeda(subtotal)}</dd></div>
      <div className="flex justify-between"><dt>Descontos</dt><dd>−{formatarMoeda(desconto)}</dd></div>
      <div className="flex justify-between"><dt>IVA</dt><dd>{formatarMoeda(impostos)}</dd></div>
      <div className="flex justify-between text-lg font-semibold"><dt>Total</dt><dd>{formatarMoeda(total)}</dd></div>
      {estimativa && <p className="text-xs text-slate-500">Estimativa; os valores finais são calculados pelo servidor ao guardar.</p>}
    </dl>
  )
}

function FormCompra({ compra, fornecedores, armazens, artigos, unidades, aoCancelar, aoGuardar }: {
  compra?: Compra
  fornecedores: Fornecedor[]
  armazens: Armazem[]
  artigos: Artigo[]
  unidades: Unidade[]
  aoCancelar: () => void
  aoGuardar: () => Promise<void>
}) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [fornecedorId, setFornecedorId] = useState(compra?.fornecedorId ?? fornecedores[0]?.id ?? 0)
  const [documento, setDocumento] = useState(compra?.numeroDocumento ?? '')
  const [data, setData] = useState(compra?.dataDocumento ?? hojeIso())
  const [vencimento, setVencimento] = useState(compra?.dataVencimento ?? '')
  const [armazemId, setArmazemId] = useState(compra?.armazemId ?? armazens.find((a) => a.principal)?.id ?? armazens[0]?.id ?? 0)
  const [observacoes, setObservacoes] = useState(compra?.observacoes ?? '')
  const [linhas, setLinhas] = useState<LinhaForm[]>(compra?.itens.map((i) => ({
    artigoId: i.artigoId, quantidade: String(i.quantidade), unidadeId: i.unidadeMedidaId, custo: String(i.custoUnitario), desconto: String(i.desconto), iva: String(i.taxaImpostoPercentagem),
  })) ?? [{ artigoId: 0, quantidade: '', unidadeId: 0, custo: '', desconto: '0', iva: '16' }])
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const artigo = (id: number) => artigos.find((a) => a.id === id)
  const actualizar = (i: number, alteracao: Partial<LinhaForm>) => setLinhas((ls) => ls.map((l, j) => (j === i ? { ...l, ...alteracao } : l)))
  const r2 = (v: number) => Math.round(v * 100) / 100

  // Pré-visualização (o servidor recalcula ao guardar).
  const previsto = useMemo(() => linhas.reduce((t, l) => {
    const bruto = r2(Number(l.quantidade) * Number(l.custo)) || 0
    const desc = r2(Number(l.desconto)) || 0
    const iva = r2((bruto - desc) * (Number(l.iva) || 0) / 100)
    return { subtotal: t.subtotal + bruto, desconto: t.desconto + desc, impostos: t.impostos + iva, total: t.total + bruto - desc + iva }
  }, { subtotal: 0, desconto: 0, impostos: 0, total: 0 }), [linhas])

  const guardar = async () => {
    const validas = linhas.filter((l) => l.artigoId)
    if (!fornecedorId) return setErro('Seleccione o fornecedor (crie-o no separador «Fornecedores» se necessário).')
    if (!documento.trim()) return setErro('Indique o número do documento do fornecedor.')
    if (validas.length === 0) return setErro('Indique pelo menos uma linha.')
    if (validas.some((l) => !(Number(l.quantidade) > 0) || !(Number(l.custo) >= 0) || !(Number(l.desconto) >= 0) || !l.unidadeId))
      return setErro('Verifique as linhas: quantidade positiva, custo e desconto zero ou positivos.')
    setErro(null)
    setAGuardar(true)
    const corpo = {
      fornecedorId, numeroDocumento: documento.trim(), dataDocumento: data, dataVencimento: vencimento || null, armazemId,
      observacoes: observacoes.trim() || null,
      itens: validas.map((l) => ({ artigoId: l.artigoId, quantidade: Number(l.quantidade), unidadeMedidaId: l.unidadeId, custoUnitario: Number(l.custo), desconto: Number(l.desconto) || 0, taxaImpostoPercentagem: Number(l.iva) || 0 })),
      rowVersion: compra?.rowVersion ?? null,
    }
    try {
      if (compra) await api.put(`/api/compras/${compra.id}`, corpo)
      else await api.post('/api/compras', corpo)
      avisos.sucesso('Rascunho guardado. Confirme a compra para dar entrada no stock.')
      await aoGuardar()
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <div className="space-y-4">
      {erro && <Alerta tipo="erro">{erro}</Alerta>}
      {fornecedores.length === 0 && <Alerta tipo="info">Ainda não há fornecedores activos. Crie-os no separador «Fornecedores».</Alerta>}
      <div className="grid gap-4 sm:grid-cols-3">
        <div className="sm:col-span-2">
          <label htmlFor="fornCompra" className="rotulo">Fornecedor</label>
          <select id="fornCompra" className="campo" value={fornecedorId} onChange={(e) => setFornecedorId(Number(e.target.value))}>
            {fornecedores.map((f) => <option key={f.id} value={f.id}>{f.nome}{f.nuit && ` (NUIT ${f.nuit})`}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="docCompra" className="rotulo">N.º do documento (factura)</label>
          <input id="docCompra" className="campo" maxLength={50} value={documento} onChange={(e) => setDocumento(e.target.value)} />
        </div>
        <div>
          <label htmlFor="dataCompra" className="rotulo">Data do documento</label>
          <input id="dataCompra" type="date" className="campo" max={hojeIso()} value={data} onChange={(e) => setData(e.target.value)} />
        </div>
        <div>
          <label htmlFor="vencCompra" className="rotulo">Vencimento (opcional)</label>
          <input id="vencCompra" type="date" className="campo" min={data} value={vencimento} onChange={(e) => setVencimento(e.target.value)} />
        </div>
        <div>
          <label htmlFor="armCompra" className="rotulo">Armazém de entrada</label>
          <select id="armCompra" className="campo" value={armazemId} onChange={(e) => setArmazemId(Number(e.target.value))}>
            {armazens.map((a) => <option key={a.id} value={a.id}>{a.nome}</option>)}
          </select>
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full min-w-[760px] text-sm">
          <thead className="text-left text-slate-600">
            <tr>
              <th scope="col" className="py-2">Artigo</th>
              <th scope="col" className="w-24 py-2">Qtd.</th>
              <th scope="col" className="w-24 py-2">Unidade</th>
              <th scope="col" className="w-32 py-2">Custo unit. s/ IVA</th>
              <th scope="col" className="w-28 py-2">Desconto (MT)</th>
              <th scope="col" className="w-20 py-2">IVA %</th>
              <th scope="col" className="w-12 py-2"><span className="sr-only">Remover</span></th>
            </tr>
          </thead>
          <tbody>
            {linhas.map((l, i) => {
              const a = artigo(l.artigoId)
              return (
                <tr key={i}>
                  <td className="py-1 pr-2">
                    <select className="campo" value={l.artigoId} aria-label={`Artigo ${i + 1}`}
                      onChange={(e) => { const novo = artigo(Number(e.target.value)); actualizar(i, { artigoId: novo?.id ?? 0, unidadeId: novo?.unidadeMedidaId ?? 0 }) }}>
                      <option value={0}>Seleccione…</option>
                      {artigos.map((x) => <option key={x.id} value={x.id}>{x.nome} ({x.unidade})</option>)}
                    </select>
                  </td>
                  <td className="py-1 pr-2"><input type="number" min={0} step="0.001" className="campo" aria-label={`Quantidade ${i + 1}`} value={l.quantidade} onChange={(e) => actualizar(i, { quantidade: e.target.value })} /></td>
                  <td className="py-1 pr-2">
                    <select className="campo" value={l.unidadeId} disabled={!a} aria-label={`Unidade ${i + 1}`} onChange={(e) => actualizar(i, { unidadeId: Number(e.target.value) })}>
                      {unidades.filter((u) => a && u.dimensao === a.dimensao).map((u) => <option key={u.id} value={u.id}>{u.codigo}</option>)}
                    </select>
                  </td>
                  <td className="py-1 pr-2"><input type="number" min={0} step="0.0001" className="campo" aria-label={`Custo ${i + 1}`} value={l.custo} onChange={(e) => actualizar(i, { custo: e.target.value })} /></td>
                  <td className="py-1 pr-2"><input type="number" min={0} step="0.01" className="campo" aria-label={`Desconto ${i + 1}`} value={l.desconto} onChange={(e) => actualizar(i, { desconto: e.target.value })} /></td>
                  <td className="py-1 pr-2"><input type="number" min={0} max={100} step="0.01" className="campo" aria-label={`IVA ${i + 1}`} value={l.iva} onChange={(e) => actualizar(i, { iva: e.target.value })} /></td>
                  <td className="py-1">
                    <button type="button" className="btn-icone" aria-label={`Remover linha ${i + 1}`} disabled={linhas.length === 1} onClick={() => setLinhas((ls) => ls.filter((_, j) => j !== i))}>
                      <Trash2 className="h-4 w-4 text-red-600" />
                    </button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      <button type="button" className="btn-secundario" onClick={() => setLinhas((ls) => [...ls, { artigoId: 0, quantidade: '', unidadeId: 0, custo: '', desconto: '0', iva: '16' }])}>
        <Plus className="h-4 w-4" aria-hidden /> Linha
      </button>
      <div className="flex flex-wrap items-start gap-4">
        <div className="min-w-60 flex-1">
          <label htmlFor="obsCompra" className="rotulo">Observações</label>
          <textarea id="obsCompra" rows={2} className="campo py-2" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
        </div>
        <Totais {...previsto} estimativa />
      </div>
      <div className="flex justify-end gap-2">
        <button type="button" className="btn-secundario" onClick={aoCancelar}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar rascunho
        </button>
      </div>
    </div>
  )
}

function Fornecedores({ podeGerir }: { podeGerir: boolean }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const lista = useDados(() => api.get<Fornecedor[]>('/api/fornecedores' + query({ pesquisa: pesquisaAtrasada, incluirInactivos: true })), [pesquisaAtrasada])
  const [editar, setEditar] = useState<Fornecedor | 'novo' | null>(null)

  const alternar = async (f: Fornecedor) => {
    try {
      await api.post(`/api/fornecedores/${f.id}/activo`, { activo: !f.activo })
      avisos.sucesso(f.activo ? 'Fornecedor desactivado.' : 'Fornecedor activado.')
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="cartao">
      <div className="flex flex-wrap items-center gap-3 border-b border-slate-200 p-4">
        <div className="relative min-w-60 flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input type="search" className="campo pl-9" placeholder="Nome, NUIT ou telefone…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar fornecedores" />
        </div>
        {podeGerir && <button type="button" className="btn-primario" onClick={() => setEditar('novo')} disabled={!podeGravar}><Plus className="h-4 w-4" aria-hidden /> Novo fornecedor</button>}
      </div>
      {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados?.length === 0 && <Vazio texto="Sem fornecedores." />}
      {lista.dados && lista.dados.length > 0 && (
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-slate-600">
            <tr>
              <th scope="col" className="px-4 py-3">Fornecedor</th>
              <th scope="col" className="px-4 py-3">Contactos</th>
              <th scope="col" className="px-4 py-3 text-right">Compras confirmadas</th>
              <th scope="col" className="px-4 py-3 text-right">Acções</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {lista.dados.map((f) => (
              <tr key={f.id} className={f.activo ? '' : 'opacity-60'}>
                <td className="px-4 py-2"><p className="font-medium">{f.nome}</p><p className="text-xs text-slate-500">{f.nuit ? `NUIT ${f.nuit}` : 'Sem NUIT'}{!f.activo && ' · inactivo'}</p></td>
                <td className="px-4 py-2 text-slate-600">{[f.telefone, f.email].filter(Boolean).join(' · ') || '—'}</td>
                <td className="px-4 py-2 text-right tabular-nums">{f.numeroCompras} · {formatarMoeda(f.totalCompras)}</td>
                <td className="px-4 py-2">
                  {podeGerir && (
                    <div className="flex justify-end">
                      <button type="button" className="btn-icone" aria-label={`Editar ${f.nome}`} disabled={!podeGravar} onClick={() => setEditar(f)}><Pencil className="h-4 w-4" /></button>
                      <button type="button" className="btn-icone" aria-label={`${f.activo ? 'Desactivar' : 'Activar'} ${f.nome}`} disabled={!podeGravar} onClick={() => void alternar(f)}>
                        <Power className={`h-4 w-4 ${f.activo ? 'text-red-600' : 'text-green-700'}`} />
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editar && <FormFornecedor fornecedor={editar === 'novo' ? undefined : editar} aoFechar={(r) => { setEditar(null); if (r) void lista.recarregar() }} />}
    </div>
  )
}

function FormFornecedor({ fornecedor, aoFechar }: { fornecedor?: Fornecedor; aoFechar: (recarregar?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [dados, setDados] = useState({
    nome: fornecedor?.nome ?? '', nuit: fornecedor?.nuit ?? '', telefone: fornecedor?.telefone ?? '', email: fornecedor?.email ?? '',
    endereco: fornecedor?.endereco ?? '', observacoes: fornecedor?.observacoes ?? '',
  })
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const campo = (k: keyof typeof dados) => ({ value: dados[k], onChange: (e: { target: { value: string } }) => setDados((d) => ({ ...d, [k]: e.target.value })) })

  const guardar = async () => {
    if (!dados.nome.trim()) return setErro('Indique o nome do fornecedor.')
    if (dados.nuit && !/^\d{9}$/.test(dados.nuit.trim())) return setErro('O NUIT tem 9 algarismos.')
    setErro(null)
    setAGuardar(true)
    const corpo = Object.fromEntries(Object.entries(dados).map(([k, v]) => [k, v.trim() || null]))
    try {
      if (fornecedor) await api.put(`/api/fornecedores/${fornecedor.id}`, corpo)
      else await api.post('/api/fornecedores', corpo)
      avisos.sucesso(fornecedor ? 'Fornecedor actualizado.' : 'Fornecedor criado.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={fornecedor ? `Editar ${fornecedor.nome}` : 'Novo fornecedor'} aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="grid gap-4 sm:grid-cols-2">
        {erro && <div className="sm:col-span-2"><Alerta tipo="erro">{erro}</Alerta></div>}
        <div className="sm:col-span-2"><label htmlFor="fNome" className="rotulo">Nome</label><input id="fNome" className="campo" maxLength={150} {...campo('nome')} /></div>
        <div><label htmlFor="fNuit" className="rotulo">NUIT (opcional)</label><input id="fNuit" className="campo" inputMode="numeric" maxLength={9} {...campo('nuit')} /></div>
        <div><label htmlFor="fTel" className="rotulo">Telefone</label><input id="fTel" type="tel" className="campo" maxLength={30} {...campo('telefone')} /></div>
        <div className="sm:col-span-2"><label htmlFor="fMail" className="rotulo">E-mail</label><input id="fMail" type="email" className="campo" maxLength={200} {...campo('email')} /></div>
        <div className="sm:col-span-2"><label htmlFor="fEnd" className="rotulo">Endereço</label><input id="fEnd" className="campo" maxLength={1000} {...campo('endereco')} /></div>
        <div className="sm:col-span-2"><label htmlFor="fObs" className="rotulo">Observações</label><textarea id="fObs" rows={2} className="campo py-2" maxLength={1000} {...campo('observacoes')} /></div>
      </div>
    </Modal>
  )
}
