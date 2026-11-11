import { useState } from 'react'
import { Loader2, Plus, Search, Trash2, X } from 'lucide-react'
import { api, novaChaveIdempotencia, query } from '../../api/cliente'
import { Alerta, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { FormCliente } from '../clientes/PaginaClientes'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Cliente, DocumentoComercial, Pagina, Produto, TaxaImposto } from '../../types/api'
import { hojeIso, somarDias } from '../../utils/datas'
import { formatarMoeda } from '../../utils/formatar'

type ClienteEscolhido = Pick<Cliente, 'id' | 'nome' | 'nuit'>

/** Escolha de um cliente da ficha de clientes (pesquisa por nome, telefone ou NUIT). */
export function SeletorCliente({ valor, aoMudar, rotulo = 'Cliente', permiteCriar = false }: {
  valor: ClienteEscolhido | null
  aoMudar: (c: ClienteEscolhido | null) => void
  rotulo?: string
  /** Se o cliente não estiver na lista, abre a ficha para o criar ali mesmo. */
  permiteCriar?: boolean
}) {
  const { tem } = useSessao()
  const [pesquisa, setPesquisa] = useState('')
  const [criar, setCriar] = useState(false)
  const atrasada = useAtraso(pesquisa)
  const resultados = useDados(
    () => (atrasada.trim().length >= 2 ? api.get<Pagina<Cliente>>('/api/clientes' + query({ pesquisa: atrasada.trim(), activo: true, tamanho: 10 })) : Promise.resolve(null)),
    [atrasada])

  const podeCriar = permiteCriar && tem('clientes.gerir')
  if (valor)
    return (
      <div>
        <p className="rotulo">{rotulo}</p>
        <div className="flex min-h-11 items-center justify-between rounded-lg border border-slate-300 px-3">
          <span><span className="font-medium">{valor.nome}</span>{valor.nuit && <span className="text-slate-500"> · NUIT {valor.nuit}</span>}</span>
          <button type="button" className="btn-icone" onClick={() => aoMudar(null)} aria-label="Trocar de cliente"><X className="h-4 w-4" /></button>
        </div>
      </div>
    )
  if (criar) return <FormCliente nomeInicial={pesquisa.trim()} aoFechar={(c) => { setCriar(false); if (c) aoMudar(c) }} />
  return (
    <div className="relative">
      {podeCriar && (
        <button type="button" className="absolute right-0 top-0 inline-flex items-center gap-1 text-sm font-medium text-primaria-700" onClick={() => setCriar(true)}>
          <Plus className="h-4 w-4" aria-hidden /> Novo cliente
        </button>
      )}
      <label htmlFor="selCliente" className="rotulo">{rotulo}</label>
      <Search className="pointer-events-none absolute bottom-3.5 left-3 h-4 w-4 text-slate-400" aria-hidden />
      <input id="selCliente" className="campo pl-9" placeholder="Pesquisar nome, telefone ou NUIT (mín. 2 letras)" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} autoComplete="off" />
      {resultados.dados && (
        <ul className="absolute z-10 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white shadow-lg">
          {resultados.dados.itens.length === 0 && !podeCriar && <li className="px-3 py-2 text-sm text-slate-500">Sem clientes. Crie-o primeiro em Clientes.</li>}
          {resultados.dados.itens.length === 0 && podeCriar && <li className="px-3 py-2 text-sm text-slate-500">Cliente não encontrado.</li>}
          {resultados.dados.itens.map((c) => (
            <li key={c.id}>
              <button type="button" className="w-full px-3 py-2 text-left text-sm hover:bg-slate-50" onClick={() => { aoMudar(c); setPesquisa('') }}>
                <span className="font-medium">{c.nome}</span>
                <span className="text-slate-500">{c.nuit ? ` · NUIT ${c.nuit}` : ' · sem NUIT'}{c.telefone && ` · ${c.telefone}`}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

interface LinhaForm {
  chave: string
  produtoId: number | null
  descricao: string
  quantidade: string
  preco: string
  desconto: string
  /** Linha livre: taxa de IVA ('' = isento). Linhas de produto usam a taxa do produto. */
  taxaImpostoId: string
  percentagem: number
}

const numero = (texto: string) => Number(texto.replace(',', '.'))

/** Pré-visualização da linha (o servidor recalcula e é o valor que fica no documento). */
function totalLinha(l: LinhaForm) {
  const bruto = Math.round(numero(l.preco) * numero(l.quantidade) * 100) / 100
  return Math.max(0, bruto - (numero(l.desconto) || 0))
}

/** Factura a crédito (cliente da ficha obrigatório) ou cotação (cliente da ficha ou só nome/NUIT). */
export function FormDocumento({ tipo, aoFechar, aoEmitir }: { tipo: 'Factura' | 'Cotacao'; aoFechar: () => void; aoEmitir: (id: number) => void }) {
  const { podeGravar } = useLigacao()
  const factura = tipo === 'Factura'
  const taxas = useDados(() => api.get<TaxaImposto[]>('/api/catalogo/taxas'), [])
  const [cliente, setCliente] = useState<ClienteEscolhido | null>(null)
  const [nomeLivre, setNomeLivre] = useState('')
  const [nuitLivre, setNuitLivre] = useState('')
  const [data, setData] = useState(() => somarDias(hojeIso(), factura ? 30 : 15))
  const [observacoes, setObservacoes] = useState('')
  const [linhas, setLinhas] = useState<LinhaForm[]>([])
  const [pesquisaProduto, setPesquisaProduto] = useState('')
  const atrasada = useAtraso(pesquisaProduto)
  const produtos = useDados(
    () => (atrasada.trim().length >= 2 ? api.get<Pagina<Produto>>('/api/produtos' + query({ activo: true, pesquisa: atrasada.trim(), tamanho: 10 })) : Promise.resolve(null)),
    [atrasada])
  const [chave] = useState(novaChaveIdempotencia)
  const [erro, setErro] = useState<string | null>(null)
  const [aEmitir, setAEmitir] = useState(false)
  const activas = taxas.dados?.filter((t) => t.activo) ?? []
  const total = linhas.reduce((s, l) => s + totalLinha(l), 0)

  const alterar = (k: string, mudanca: Partial<LinhaForm>) => setLinhas(linhas.map((l) => (l.chave === k ? { ...l, ...mudanca } : l)))
  const adicionarProduto = (p: Produto) => {
    setLinhas([...linhas, {
      chave: novaChaveIdempotencia(), produtoId: p.id, descricao: p.nome, quantidade: '1', preco: String(p.precoVenda).replace('.', ','), desconto: '',
      taxaImpostoId: '', percentagem: p.taxaImpostoPercentagem ?? 0,
    }])
    setPesquisaProduto('')
  }
  const adicionarLivre = () => {
    const iva = activas.find((t) => t.percentagem === 16) ?? activas[0]
    setLinhas([...linhas, { chave: novaChaveIdempotencia(), produtoId: null, descricao: '', quantidade: '1', preco: '', desconto: '', taxaImpostoId: iva ? String(iva.id) : '', percentagem: iva?.percentagem ?? 0 }])
  }

  const emitir = async () => {
    if (factura && !cliente) return setErro('Escolha o cliente: a factura fica a crédito na conta dele.')
    if (linhas.length === 0) return setErro('Junte pelo menos uma linha.')
    const invalida = linhas.findIndex((l) => !(numero(l.quantidade) > 0) || !(numero(l.preco) >= 0) || (!l.produtoId && !l.descricao.trim()))
    if (invalida >= 0) return setErro(`Linha ${invalida + 1}: indique a descrição, a quantidade e o preço.`)
    setErro(null)
    setAEmitir(true)
    try {
      const doc = await api.post<DocumentoComercial>(factura ? '/api/documentos/facturas' : '/api/documentos/cotacoes', {
        clienteId: cliente?.id ?? null,
        clienteNome: cliente ? null : nomeLivre.trim() || null,
        clienteNuit: cliente ? null : nuitLivre.trim() || null,
        clienteEndereco: null,
        data,
        observacoes: observacoes.trim() || null,
        linhas: linhas.map((l) => ({
          produtoId: l.produtoId, descricao: l.descricao.trim() || null, quantidade: numero(l.quantidade), precoUnitario: numero(l.preco),
          desconto: numero(l.desconto) || null, taxaImpostoId: l.produtoId ? null : l.taxaImpostoId ? Number(l.taxaImpostoId) : null,
        })),
      }, { 'Idempotency-Key': chave })
      aoEmitir(doc.id)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAEmitir(false)
    }
  }

  return (
    <Modal titulo={factura ? 'Nova factura' : 'Nova cotação'} aoFechar={aoFechar} largura="max-w-5xl"
      rodape={<>
        <span className="mr-auto self-center text-sm text-slate-600">Total previsto: <span className="font-semibold tabular-nums">{formatarMoeda(total)}</span> (IVA incluído)</span>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aEmitir || !podeGravar} onClick={() => void emitir()}>
          {aEmitir && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} {factura ? 'Emitir factura' : 'Emitir cotação'}
        </button>
      </>}>
      <div className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        {factura && <Alerta tipo="info">A factura fica a crédito: o cliente paga depois e o pagamento regista-se com um recibo. Para vendas pagas no momento use o POS (emite a venda a dinheiro).</Alerta>}
        <div className="grid gap-4 md:grid-cols-3">
          <div className="md:col-span-2">
            <SeletorCliente valor={cliente} aoMudar={setCliente} rotulo={factura ? 'Cliente (da ficha de clientes)' : 'Cliente da ficha (opcional)'} />
          </div>
          <div>
            <label htmlFor="fData" className="rotulo">{factura ? 'Vencimento' : 'Válida até'}</label>
            <input id="fData" type="date" className="campo" min={hojeIso()} value={data} onChange={(e) => setData(e.target.value)} />
          </div>
          {!factura && !cliente && (
            <>
              <div className="md:col-span-2"><label htmlFor="fNome" className="rotulo">Ou só o nome do cliente</label><input id="fNome" className="campo" maxLength={200} value={nomeLivre} onChange={(e) => setNomeLivre(e.target.value)} placeholder="Vazio = Consumidor final" /></div>
              <div><label htmlFor="fNuit" className="rotulo">NUIT (opcional)</label><input id="fNuit" className="campo" inputMode="numeric" maxLength={9} value={nuitLivre} onChange={(e) => setNuitLivre(e.target.value.replace(/\D/g, ''))} /></div>
            </>
          )}
        </div>

        <div className="flex flex-wrap items-end gap-2">
          <div className="relative min-w-72 flex-1">
            <label htmlFor="fProd" className="rotulo">Juntar produto do catálogo</label>
            <Search className="pointer-events-none absolute bottom-3.5 left-3 h-4 w-4 text-slate-400" aria-hidden />
            <input id="fProd" className="campo pl-9" placeholder="Nome ou código (mín. 2 letras)" value={pesquisaProduto} onChange={(e) => setPesquisaProduto(e.target.value)} autoComplete="off" />
            {produtos.dados && (
              <ul className="absolute z-10 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white shadow-lg">
                {produtos.dados.itens.length === 0 && <li className="px-3 py-2 text-sm text-slate-500">Sem produtos.</li>}
                {produtos.dados.itens.map((p) => (
                  <li key={p.id}>
                    <button type="button" className="flex w-full justify-between px-3 py-2 text-left text-sm hover:bg-slate-50" onClick={() => adicionarProduto(p)}>
                      <span>{p.nome} <span className="text-slate-500">· {p.codigo}</span></span><span className="tabular-nums">{formatarMoeda(p.precoVenda)}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <button type="button" className="btn-secundario" onClick={adicionarLivre}><Plus className="h-4 w-4" aria-hidden /> Linha livre</button>
        </div>

        {linhas.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-slate-600">
                <tr>
                  <th scope="col" className="px-2 py-2">Descrição</th>
                  <th scope="col" className="w-24 px-2 py-2">Qtd.</th>
                  <th scope="col" className="w-32 px-2 py-2">Preço (c/ IVA)</th>
                  <th scope="col" className="w-28 px-2 py-2">Desconto</th>
                  <th scope="col" className="w-36 px-2 py-2">IVA</th>
                  <th scope="col" className="w-32 px-2 py-2 text-right">Total</th>
                  <th scope="col" className="w-10 px-2 py-2"><span className="sr-only">Retirar</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {linhas.map((l, n) => (
                  <tr key={l.chave}>
                    <td className="px-2 py-1"><input className="campo" maxLength={300} value={l.descricao} aria-label={`Descrição da linha ${n + 1}`} onChange={(e) => alterar(l.chave, { descricao: e.target.value })} /></td>
                    <td className="px-2 py-1"><input className="campo text-right" inputMode="decimal" value={l.quantidade} aria-label={`Quantidade da linha ${n + 1}`} onChange={(e) => alterar(l.chave, { quantidade: e.target.value })} /></td>
                    <td className="px-2 py-1"><input className="campo text-right" inputMode="decimal" value={l.preco} aria-label={`Preço da linha ${n + 1}`} onChange={(e) => alterar(l.chave, { preco: e.target.value })} /></td>
                    <td className="px-2 py-1"><input className="campo text-right" inputMode="decimal" value={l.desconto} placeholder="0" aria-label={`Desconto da linha ${n + 1}`} onChange={(e) => alterar(l.chave, { desconto: e.target.value })} /></td>
                    <td className="px-2 py-1">
                      {l.produtoId
                        ? <span className="text-slate-600">{l.percentagem} % (produto)</span>
                        : (
                          <select className="campo" value={l.taxaImpostoId} aria-label={`IVA da linha ${n + 1}`}
                            onChange={(e) => alterar(l.chave, { taxaImpostoId: e.target.value, percentagem: activas.find((t) => String(t.id) === e.target.value)?.percentagem ?? 0 })}>
                            <option value="">Isento (0 %)</option>
                            {activas.map((t) => <option key={t.id} value={t.id}>{t.nome}</option>)}
                          </select>
                        )}
                    </td>
                    <td className="px-2 py-1 text-right font-medium tabular-nums">{formatarMoeda(totalLinha(l))}</td>
                    <td className="px-2 py-1">
                      <button type="button" className="btn-icone" onClick={() => setLinhas(linhas.filter((x) => x.chave !== l.chave))} aria-label={`Retirar linha ${n + 1}`}><Trash2 className="h-4 w-4" /></button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div><label htmlFor="fObs" className="rotulo">Observações (saem no documento)</label><textarea id="fObs" className="campo min-h-16 py-2" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} /></div>
      </div>
    </Modal>
  )
}
