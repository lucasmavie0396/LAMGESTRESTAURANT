import { useState } from 'react'
import { Loader2, Plus, Settings2 } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { BarrasHorizontais } from '../../components/Graficos'
import { Alerta, Carregando, Etiqueta, Modal, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { CategoriaDespesa, Despesa, MetodoPagamento, Pagina, ResumoDespesas, SessaoCaixa } from '../../types/api'
import { hojeIso, somarDias } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { lerTerminalLocal } from '../../utils/terminalLocal'
import { DialogoMotivo } from '../pos/Dialogos'
import { naoCredito } from '../../utils/metodos'

export function PaginaDespesas() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [desde, setDesde] = useState(() => `${hojeIso().slice(0, 7)}-01`)
  const [ate, setAte] = useState(hojeIso)
  const [categoriaId, setCategoriaId] = useState('')
  const [pagina, setPagina] = useState(1)
  const [nova, setNova] = useState(false)
  const [gerirCategorias, setGerirCategorias] = useState(false)
  const [anular, setAnular] = useState<Despesa | null>(null)
  const categorias = useDados(() => api.get<CategoriaDespesa[]>('/api/despesas/categorias'), [])
  const lista = useDados(() => api.get<Pagina<Despesa>>('/api/despesas' + query({ desde, ate, categoriaId, pagina, tamanho: 50 })), [desde, ate, categoriaId, pagina])
  const resumo = useDados(() => api.get<ResumoDespesas>('/api/despesas/resumo' + query({ desde, ate })), [desde, ate])

  const recarregar = async () => { await Promise.all([lista.recarregar(), resumo.recarregar()]) }

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Despesas</h1>
        <div className="flex gap-2">
          {tem('despesas.anular') && <button type="button" className="btn-secundario" onClick={() => setGerirCategorias(true)}><Settings2 className="h-4 w-4" aria-hidden /> Categorias</button>}
          <button type="button" className="btn-primario" onClick={() => setNova(true)} disabled={!podeGravar || !categorias.dados?.some((c) => c.activo)}><Plus className="h-4 w-4" aria-hidden /> Nova despesa</button>
        </div>
      </div>
      <div className="flex flex-wrap items-end gap-3">
        <label className="text-sm">De<input type="date" className="campo" value={desde} max={ate} onChange={(e) => { setDesde(e.target.value); setPagina(1) }} /></label>
        <label className="text-sm">Até<input type="date" className="campo" value={ate} min={desde} onChange={(e) => { setAte(e.target.value); setPagina(1) }} /></label>
        <select className="campo w-56" value={categoriaId} onChange={(e) => { setCategoriaId(e.target.value); setPagina(1) }} aria-label="Categoria">
          <option value="">Todas as categorias</option>
          {categorias.dados?.map((c) => <option key={c.id} value={c.id}>{c.nome}</option>)}
        </select>
        <button type="button" className="btn-secundario min-h-11" onClick={() => { setDesde(somarDias(hojeIso(), -6)); setAte(hojeIso()) }}>7 dias</button>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <section className="cartao p-4">
          <h2 className="font-semibold">Total no período</h2>
          <p className="mt-1 text-2xl font-semibold tabular-nums">{formatarMoeda(resumo.dados?.total ?? 0)}</p>
          <p className="mb-3 text-xs text-slate-500">{resumo.dados?.numero ?? 0} despesas confirmadas (anuladas excluídas)</p>
          <BarrasHorizontais linhas={(resumo.dados?.porCategoria ?? []).map((l) => ({ rotulo: l.rotulo, valor: l.valor, extra: `(${l.quantidade})` }))} vazio="Sem despesas no período." />
        </section>
        <section className="cartao lg:col-span-2">
          {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
          {!lista.dados && !lista.erro && <Carregando />}
          {lista.dados?.itens.length === 0 && <Vazio texto="Sem despesas no período." />}
          {lista.dados && lista.dados.itens.length > 0 && (
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-slate-600">
                <tr>
                  <th scope="col" className="px-4 py-3">Data</th>
                  <th scope="col" className="px-4 py-3">Descrição</th>
                  <th scope="col" className="px-4 py-3 text-right">Valor</th>
                  <th scope="col" className="px-4 py-3"><span className="sr-only">Acções</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {lista.dados.itens.map((d) => (
                  <tr key={d.id} className={d.estado === 'Anulada' ? 'opacity-60' : ''}>
                    <td className="whitespace-nowrap px-4 py-2">{formatarDataHora(d.dataUtc)}</td>
                    <td className="px-4 py-2">
                      <p className={`font-medium ${d.estado === 'Anulada' ? 'line-through' : ''}`}>{d.descricao}</p>
                      <p className="text-xs text-slate-500">
                        {d.categoria} · {d.metodoPagamento}{d.referencia && ` (${d.referencia})`}{d.sessaoCaixaId && ` · caixa n.º ${d.sessaoCaixaId}`}{d.utilizador && ` · ${d.utilizador}`}
                      </p>
                      {d.motivoAnulacao && <p className="text-xs text-red-700">Anulada: {d.motivoAnulacao}</p>}
                    </td>
                    <td className="px-4 py-2 text-right font-medium tabular-nums">{formatarMoeda(d.valor)}</td>
                    <td className="px-4 py-2 text-right">
                      {d.estado === 'Anulada' ? <Etiqueta cor="cinza">Anulada</Etiqueta> : tem('despesas.anular') && (
                        <button type="button" className="text-sm font-medium text-red-700 hover:underline disabled:opacity-50" disabled={!podeGravar} onClick={() => setAnular(d)}>Anular</button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {lista.dados && lista.dados.total > 0 && <Paginacao pagina={pagina} tamanho={50} total={lista.dados.total} aoMudar={setPagina} />}
        </section>
      </div>

      {nova && categorias.dados && <FormDespesa categorias={categorias.dados.filter((c) => c.activo)} aoFechar={(ok) => { setNova(false); if (ok) void recarregar() }} />}
      {anular && (
        <DialogoMotivo titulo="Anular despesa" texto="Anular despesa" aoFechar={() => setAnular(null)}
          descricao={anular.sessaoCaixaId ? 'Foi paga pela caixa: se a sessão ainda estiver aberta, o valor volta à caixa com uma entrada.' : 'A despesa deixa de contar nos totais. Fica registada na auditoria.'}
          aoConfirmar={async (motivo) => {
            try {
              await api.post(`/api/despesas/${anular.id}/anular`, { motivo, rowVersion: anular.rowVersion })
              avisos.sucesso('Despesa anulada.')
              setAnular(null)
              await recarregar()
            } catch (e) {
              avisos.erro(mensagemErro(e))
            }
          }} />
      )}
      {gerirCategorias && <GestaoCategorias aoFechar={() => { setGerirCategorias(false); void categorias.recarregar() }} />}
    </div>
  )
}

function FormDespesa({ categorias, aoFechar }: { categorias: CategoriaDespesa[]; aoFechar: (ok?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const { tem } = useSessao()
  const metodos = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento').then((l) => l.filter(naoCredito)), [])
  const terminalId = lerTerminalLocal()
  const sessao = useDados(() => (terminalId && tem('caixa.operar') ? api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`) : Promise.resolve(undefined)), [terminalId])
  const [categoriaId, setCategoriaId] = useState(categorias[0]?.id ?? 0)
  const [data, setData] = useState(hojeIso)
  const [valor, setValor] = useState('')
  const [descricao, setDescricao] = useState('')
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [referencia, setReferencia] = useState('')
  const [pelaCaixa, setPelaCaixa] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const activos = metodos.dados?.filter((m) => m.activo) ?? []
  const metodo = metodoId ?? activos[0]?.id ?? null
  const podePelaCaixa = Boolean(sessao.dados)

  const guardar = async () => {
    if (!(Number(valor) > 0)) return setErro('Indique um valor positivo.')
    if (descricao.trim().length < 3) return setErro('Indique a descrição (pelo menos 3 caracteres).')
    setErro(null)
    setAGuardar(true)
    try {
      await api.post('/api/despesas', {
        categoriaDespesaId: categoriaId, data: pelaCaixa ? hojeIso() : data, valor: Number(valor), descricao: descricao.trim(),
        metodoPagamentoId: metodo, referencia: referencia.trim() || null, sessaoCaixaId: pelaCaixa ? sessao.dados?.id : null,
      })
      avisos.sucesso('Despesa registada.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo="Nova despesa" aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar || !metodo}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Registar
        </button>
      </>}>
      <div className="grid gap-4 sm:grid-cols-2">
        {erro && <div className="sm:col-span-2"><Alerta tipo="erro">{erro}</Alerta></div>}
        <div>
          <label htmlFor="dCat" className="rotulo">Categoria</label>
          <select id="dCat" className="campo" value={categoriaId} onChange={(e) => setCategoriaId(Number(e.target.value))}>
            {categorias.map((c) => <option key={c.id} value={c.id}>{c.nome}</option>)}
          </select>
        </div>
        <div><label htmlFor="dValor" className="rotulo">Valor (MT)</label><input id="dValor" type="number" min={0} step="0.01" className="campo" value={valor} onChange={(e) => setValor(e.target.value)} /></div>
        <div className="sm:col-span-2"><label htmlFor="dDesc" className="rotulo">Descrição</label><input id="dDesc" className="campo" maxLength={500} value={descricao} onChange={(e) => setDescricao(e.target.value)} placeholder="Ex.: botija de gás 11 kg" /></div>
        <div>
          <label htmlFor="dMet" className="rotulo">Pago por</label>
          <select id="dMet" className="campo" value={metodo ?? ''} onChange={(e) => setMetodoId(Number(e.target.value))}>
            {activos.map((m) => <option key={m.id} value={m.id}>{m.nome}</option>)}
          </select>
        </div>
        <div><label htmlFor="dRef" className="rotulo">Referência / n.º do documento</label><input id="dRef" className="campo" maxLength={100} value={referencia} onChange={(e) => setReferencia(e.target.value)} /></div>
        <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
          <input type="checkbox" className="h-4 w-4" checked={pelaCaixa} disabled={!podePelaCaixa} onChange={(e) => setPelaCaixa(e.target.checked)} />
          {podePelaCaixa ? `Paga com dinheiro da caixa deste terminal (sessão n.º ${sessao.dados?.id})` : 'Paga pela caixa: indisponível (sem caixa aberto neste terminal)'}
        </label>
        {!pelaCaixa && <div><label htmlFor="dData" className="rotulo">Data</label><input id="dData" type="date" className="campo" max={hojeIso()} value={data} onChange={(e) => setData(e.target.value)} /></div>}
      </div>
    </Modal>
  )
}

function GestaoCategorias({ aoFechar }: { aoFechar: () => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const lista = useDados(() => api.get<CategoriaDespesa[]>('/api/despesas/categorias'), [])
  const [nome, setNome] = useState('')
  const executar = async (accao: () => Promise<unknown>, ok: string) => {
    try {
      await accao()
      avisos.sucesso(ok)
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }
  return (
    <Modal titulo="Categorias de despesa" aoFechar={aoFechar} rodape={<button type="button" className="btn-primario" onClick={aoFechar}>Fechar</button>}>
      <div className="mb-4 flex gap-2">
        <input className="campo" placeholder="Nova categoria" maxLength={200} value={nome} onChange={(e) => setNome(e.target.value)} aria-label="Nome da nova categoria" />
        <button type="button" className="btn-secundario" disabled={!podeGravar || !nome.trim()} onClick={() => void executar(async () => { await api.post('/api/despesas/categorias', { nome: nome.trim() }); setNome('') }, 'Categoria criada.')}>Criar</button>
      </div>
      <ul className="divide-y divide-slate-100">
        {lista.dados?.map((c) => (
          <li key={c.id} className="flex min-h-11 items-center justify-between">
            <span className={c.activo ? '' : 'text-slate-400 line-through'}>{c.nome}</span>
            <button type="button" className="text-sm font-medium text-primaria-700 hover:underline" disabled={!podeGravar}
              onClick={() => void executar(() => api.post(`/api/despesas/categorias/${c.id}/activo`, { activo: !c.activo }), c.activo ? 'Categoria desactivada.' : 'Categoria activada.')}>
              {c.activo ? 'Desactivar' : 'Activar'}
            </button>
          </li>
        ))}
      </ul>
    </Modal>
  )
}
