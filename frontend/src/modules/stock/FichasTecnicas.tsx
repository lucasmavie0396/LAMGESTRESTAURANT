import { useEffect, useState } from 'react'
import { Loader2, Plus, Search, Trash2 } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Confirmacao, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Artigo, Pagina, ProdutoFicha, Receita, Unidade } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { n3 } from './comum'

export function FichasTecnicas({ unidades }: { unidades: Unidade[] }) {
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const lista = useDados(() => api.get<ProdutoFicha[]>('/api/receitas' + query({ pesquisa: pesquisaAtrasada })), [pesquisaAtrasada])
  const [produto, setProduto] = useState<ProdutoFicha | null>(null)

  return (
    <div className="cartao">
      <div className="flex flex-wrap items-center gap-3 border-b border-slate-200 p-4">
        <div className="relative min-w-60 flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input type="search" className="campo pl-9" placeholder="Pesquisar produto…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar produto" />
        </div>
        <p className="text-sm text-slate-600">Produtos sem ficha técnica não descontam stock nas vendas.</p>
      </div>
      {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados?.length === 0 && <Vazio texto="Sem produtos activos." />}
      {lista.dados && lista.dados.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-600">
              <tr>
                <th scope="col" className="px-4 py-3">Produto</th>
                <th scope="col" className="px-4 py-3">Categoria</th>
                <th scope="col" className="px-4 py-3 text-right">Preço</th>
                <th scope="col" className="px-4 py-3">Ficha</th>
                <th scope="col" className="px-4 py-3 text-right">Custo estimado</th>
                <th scope="col" className="px-4 py-3 text-right">Custo / preço</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {lista.dados.map((p) => (
                <tr key={p.produtoId} className="cursor-pointer hover:bg-slate-50" onClick={() => setProduto(p)}>
                  <td className="px-4 py-2 font-medium">{p.produto} <span className="text-xs text-slate-500">{p.codigo}</span></td>
                  <td className="px-4 py-2 text-slate-600">{p.categoria}</td>
                  <td className="px-4 py-2 text-right">{formatarMoeda(p.precoVenda)}</td>
                  <td className="px-4 py-2">
                    {p.versaoActiva ? <Etiqueta cor="verde">v{p.versaoActiva} · {p.numeroIngredientes} ingred.</Etiqueta> : <Etiqueta cor="cinza">Sem ficha</Etiqueta>}
                  </td>
                  <td className="px-4 py-2 text-right">{p.custoEstimado === null ? '—' : formatarMoeda(p.custoEstimado)}</td>
                  <td className="px-4 py-2 text-right">
                    {p.custoEstimado !== null && p.precoVenda > 0 ? `${((p.custoEstimado / p.precoVenda) * 100).toFixed(0)}%` : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {produto && <EditorFicha produto={produto} unidades={unidades} aoFechar={(r) => { setProduto(null); if (r) void lista.recarregar() }} />}
    </div>
  )
}

interface Linha { artigo: Artigo | null; quantidade: string; unidadeId: number }

function EditorFicha({ produto, unidades, aoFechar }: { produto: ProdutoFicha; unidades: Unidade[]; aoFechar: (recarregar?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const versoes = useDados(() => api.get<Receita[]>(`/api/receitas/produtos/${produto.produtoId}`), [produto.produtoId])
  const artigos = useDados(() => api.get<Pagina<Artigo>>('/api/stock/artigos?activo=true&tamanho=200'), [])
  const [linhas, setLinhas] = useState<Linha[] | null>(null)
  const [observacoes, setObservacoes] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const [retirar, setRetirar] = useState(false)

  const activa = versoes.dados?.find((v) => v.activa) ?? null
  // Ao abrir: as linhas partem da versão activa (editar cria uma nova versão).
  useEffect(() => {
    if (linhas !== null || !versoes.dados || !artigos.dados) return
    const actual = versoes.dados.find((v) => v.activa)
    setLinhas(actual
      ? actual.linhas.map((l) => ({ artigo: artigos.dados!.itens.find((a) => a.id === l.artigoId) ?? null, quantidade: String(l.quantidade), unidadeId: l.unidadeMedidaId }))
      : [{ artigo: null, quantidade: '', unidadeId: 0 }])
    setObservacoes(actual?.observacoes ?? '')
  }, [linhas, versoes.dados, artigos.dados])

  const unidadesDe = (a: Artigo | null) => (a ? unidades.filter((u) => u.dimensao === a.dimensao) : [])
  const actualizar = (i: number, alteracao: Partial<Linha>) => setLinhas((ls) => ls!.map((l, j) => (j === i ? { ...l, ...alteracao } : l)))

  const guardar = async () => {
    const validas = (linhas ?? []).filter((l) => l.artigo)
    if (validas.length === 0) return setErro('Indique pelo menos um ingrediente.')
    if (validas.some((l) => !(Number(l.quantidade) > 0) || !l.unidadeId)) return setErro('Cada ingrediente precisa de quantidade positiva e unidade.')
    setErro(null)
    setAGuardar(true)
    try {
      await api.put(`/api/receitas/produtos/${produto.produtoId}`, {
        linhas: validas.map((l) => ({ artigoId: l.artigo!.id, quantidade: Number(l.quantidade), unidadeMedidaId: l.unidadeId })),
        observacoes: observacoes.trim() || null,
      })
      avisos.sucesso(activa ? `Nova versão da ficha guardada (v${activa.versao + 1}).` : 'Ficha técnica criada.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const confirmarRetirar = async () => {
    try {
      await api.post(`/api/receitas/produtos/${produto.produtoId}/retirar`)
      avisos.sucesso('Ficha técnica retirada. O produto deixa de descontar stock.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <Modal titulo={`Ficha técnica: ${produto.produto}`} aoFechar={() => aoFechar()} largura="max-w-4xl"
      rodape={<>
        {activa && <button type="button" className="btn-secundario mr-auto text-red-700" onClick={() => setRetirar(true)} disabled={!podeGravar}>Retirar ficha</button>}
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar || !linhas}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} {activa ? 'Guardar como nova versão' : 'Guardar'}
        </button>
      </>}>
      {(versoes.erro || artigos.erro) && <Alerta tipo="erro">{versoes.erro ?? artigos.erro}</Alerta>}
      {!linhas && !versoes.erro && <Carregando />}
      {linhas && artigos.dados && (
        <div className="space-y-4">
          {erro && <Alerta tipo="erro">{erro}</Alerta>}
          {artigos.dados.itens.length === 0 && <Alerta tipo="info">Crie primeiro os artigos de stock (separador «Artigos»).</Alerta>}
          <p className="text-sm text-slate-600">Quantidades para <strong>1 unidade</strong> do produto vendido. Preço de venda: {formatarMoeda(produto.precoVenda)}.</p>
          <table className="w-full text-sm">
            <thead className="text-left text-slate-600">
              <tr>
                <th scope="col" className="py-2">Ingrediente</th>
                <th scope="col" className="w-32 py-2">Quantidade</th>
                <th scope="col" className="w-32 py-2">Unidade</th>
                <th scope="col" className="w-12 py-2"><span className="sr-only">Remover</span></th>
              </tr>
            </thead>
            <tbody>
              {linhas.map((l, i) => (
                <tr key={i}>
                  <td className="py-1 pr-2">
                    <select className="campo" value={l.artigo?.id ?? ''} aria-label={`Ingrediente ${i + 1}`}
                      onChange={(e) => {
                        const artigo = artigos.dados!.itens.find((a) => a.id === Number(e.target.value)) ?? null
                        actualizar(i, { artigo, unidadeId: artigo?.unidadeMedidaId ?? 0 })
                      }}>
                      <option value="">Seleccione…</option>
                      {artigos.dados!.itens.map((a) => <option key={a.id} value={a.id}>{a.nome} ({a.unidade})</option>)}
                    </select>
                  </td>
                  <td className="py-1 pr-2">
                    <input type="number" min={0} step="0.001" className="campo" value={l.quantidade} aria-label={`Quantidade ${i + 1}`} onChange={(e) => actualizar(i, { quantidade: e.target.value })} />
                  </td>
                  <td className="py-1 pr-2">
                    <select className="campo" value={l.unidadeId} disabled={!l.artigo} aria-label={`Unidade ${i + 1}`} onChange={(e) => actualizar(i, { unidadeId: Number(e.target.value) })}>
                      {unidadesDe(l.artigo).map((u) => <option key={u.id} value={u.id}>{u.codigo}</option>)}
                    </select>
                  </td>
                  <td className="py-1">
                    <button type="button" className="btn-icone" aria-label={`Remover ingrediente ${i + 1}`} onClick={() => setLinhas((ls) => ls!.filter((_, j) => j !== i))}>
                      <Trash2 className="h-4 w-4 text-red-600" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <button type="button" className="btn-secundario" onClick={() => setLinhas((ls) => [...ls!, { artigo: null, quantidade: '', unidadeId: 0 }])}>
            <Plus className="h-4 w-4" aria-hidden /> Ingrediente
          </button>
          <div>
            <label htmlFor="obsFicha" className="rotulo">Observações (modo de preparação, porção…)</label>
            <textarea id="obsFicha" rows={2} className="campo py-2" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
          </div>
          {versoes.dados && versoes.dados.length > 0 && (
            <details className="rounded-lg border border-slate-200 p-3">
              <summary className="cursor-pointer text-sm font-medium">Versões ({versoes.dados.length})</summary>
              <ul className="mt-2 space-y-2 text-sm">
                {versoes.dados.map((v) => (
                  <li key={v.id} className="rounded bg-slate-50 p-2">
                    <span className="font-medium">v{v.versao}</span> {v.activa && <Etiqueta cor="verde">Activa</Etiqueta>}{' '}
                    <span className="text-slate-600">desde {formatarDataHora(v.validaDesdeUtc)}{v.validaAteUtc && ` até ${formatarDataHora(v.validaAteUtc)}`}</span>
                    <span className="block text-slate-600">
                      {v.linhas.map((l) => `${n3.format(l.quantidade)} ${l.unidade} ${l.artigo}`).join(' · ')} — custo actual estimado {formatarMoeda(v.custoEstimado)}
                    </span>
                  </li>
                ))}
              </ul>
            </details>
          )}
        </div>
      )}
      {retirar && (
        <Confirmacao titulo="Retirar ficha técnica" textoConfirmar="Retirar" perigo
          mensagem="O produto deixa de descontar stock nas vendas. As versões anteriores ficam no histórico."
          aoCancelar={() => setRetirar(false)} aoConfirmar={() => void confirmarRetirar()} />
      )}
    </Modal>
  )
}
