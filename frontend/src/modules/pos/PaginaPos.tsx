import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ClipboardList, LayoutGrid, Loader2, Search, ShoppingBag, Store, Truck } from 'lucide-react'
import { api, ErroApi, novaChaveIdempotencia, query } from '../../api/cliente'
import { Alerta, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useEvento } from '../../contexts/TempoRealContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Categoria, ConfiguracoesPublicas, DocumentoImpressao, Mesa, OpcoesPos, Pagina, Pedido, PedidoItem, PedidoResumo, Produto, TipoPedido } from '../../types/api'
import { documentoVenda, formatarMoeda } from '../../utils/formatar'
import { DialogoDesconto, DialogoEntrega, DialogoMesa, DialogoMotivo, DialogoObservacoes, DialogoQuantidade, GrelhaMesas } from './Dialogos'
import { DialogoCliente } from './DialogoCliente'
import { DialogoEstorno } from './DialogoEstorno'
import { ModalPagamento } from './ModalPagamento'
import { PainelPedido } from './PainelPedido'
import { lerTerminalLocal } from '../../utils/terminalLocal'
import { imprimirNoNavegador } from '../../utils/impressaoDocumento'

/** Pedido novo ainda sem itens: só existe no servidor quando se adiciona o primeiro produto. */
/** Entre teclas do leitor de código de barras: margem para computadores lentos; à mão não se escreve um código com Enter fora de um campo. */
const INTERVALO_LEITOR_MS = 200

/** Nome do tipo de pedido no ecrã. */
function nomeTipo(tipo: TipoPedido, mesa?: string | null) {
  return tipo === 'Mesa' ? `Mesa ${mesa}` : tipo === 'TakeAway' ? 'Take Away' : tipo === 'Balcao' ? 'Balcão' : 'Delivery'
}

interface Rascunho {
  tipo: TipoPedido
  mesa?: Mesa
  nome?: string
  contacto?: string
  endereco?: string
  confirmarReservada?: boolean
  /** Chave de idempotência da criação: mantém-se até o pedido ser criado (reenvios não duplicam). */
  chave: string
}

type Dialogo =
  | { tipo: 'mesa' }
  | { tipo: 'transferir' }
  | { tipo: 'entrega'; pedido: 'TakeAway' | 'Delivery' }
  | { tipo: 'observacoes'; item: PedidoItem }
  | { tipo: 'cancelar-item'; item: PedidoItem }
  | { tipo: 'cancelar-pedido' }
  | { tipo: 'desconto' }
  | { tipo: 'pagamento' }
  | { tipo: 'abertos' }
  | { tipo: 'estorno' }
  | { tipo: 'cliente' }
  | { tipo: 'reservada'; mesa: Mesa }
  | { tipo: 'quantidade'; produto: Produto }
  | null

export function PaginaPos() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [parametros, setParametros] = useSearchParams()

  const [pedido, setPedido] = useState<Pedido | null>(null)
  const [rascunho, setRascunho] = useState<Rascunho | null>(null)
  const [aGravar, setAGravar] = useState(false)
  const [dialogo, setDialogo] = useState<Dialogo>(null)
  const [categoriaId, setCategoriaId] = useState<number | null>(null)
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa, 250)

  const categorias = useDados(() => api.get<Categoria[]>('/api/categorias'), [])
  const publicas = useDados(() => api.get<ConfiguracoesPublicas>('/api/configuracoes/publicas'), [])
  const produtos = useDados(
    () => api.get<Pagina<Produto>>('/api/produtos' + query({ activo: true, categoriaId, pesquisa: pesquisaAtrasada, tamanho: 200 })),
    [categoriaId, pesquisaAtrasada],
  )
  const mesas = useDados(() => api.get<Mesa[]>('/api/mesas'), [])
  const abertos = useDados(() => api.get<Pagina<PedidoResumo>>('/api/pedidos?estado=Aberto&tamanho=100'), [])
  // Enquanto as opções carregam, tudo aparece activo (o servidor recusa o que estiver desligado).
  const opcoes: OpcoesPos = publicas.dados?.pos ?? { mesa: true, takeAway: true, delivery: true, balcao: true, codigoBarras: true }

  const abrirPedido = useCallback(async (id: number) => {
    try {
      setPedido(await api.get<Pedido>(`/api/pedidos/${id}`))
      setRascunho(null)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }, [avisos])

  /** Abre o pedido da mesa, ou prepara um pedido novo para ela. */
  const escolherMesa = useCallback(async (mesa: Mesa, confirmarReservada = false) => {
    setDialogo(null)
    if (mesa.estado === 'Ocupada' || mesa.pedidosAbertos > 0) {
      const r = await api.get<Pagina<PedidoResumo>>(`/api/pedidos?estado=Aberto&mesaId=${mesa.id}&tamanho=1`).catch(() => null)
      if (r?.itens[0]) return void abrirPedido(r.itens[0].id)
    }
    if (mesa.estado === 'Reservada' && !confirmarReservada) return setDialogo({ tipo: 'reservada', mesa })
    setPedido(null)
    setRascunho({ tipo: 'Mesa', mesa, confirmarReservada, chave: novaChaveIdempotencia() })
  }, [abrirPedido])

  // Entrada por URL: /pos?mesa=ID (a partir do mapa de mesas), /pos?pedido=ID ou /pos?tipo=TakeAway|Delivery.
  useEffect(() => {
    const idMesa = Number(parametros.get('mesa'))
    const idPedido = Number(parametros.get('pedido'))
    const tipo = parametros.get('tipo')
    if (idPedido) void abrirPedido(idPedido)
    else if (idMesa) api.get<Mesa>(`/api/mesas/${idMesa}`).then((m) => void escolherMesa(m)).catch((e) => avisos.erro(mensagemErro(e)))
    else if (tipo === 'TakeAway' || tipo === 'Delivery') setDialogo({ tipo: 'entrega', pedido: tipo })
    if (idMesa || idPedido || tipo) setParametros({}, { replace: true })
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // Actualização em segundo plano: só se aplica se o pedido ainda for o que está no ecrã
  // (entretanto o operador pode ter começado outra venda, e esta resposta não a pode substituir).
  const pedidoNoEcra = useRef<number | null>(null)
  pedidoNoEcra.current = pedido?.id ?? null
  const actualizarNoEcra = useCallback(async (id: number) => {
    const actual = await api.get<Pedido>(`/api/pedidos/${id}`).catch(() => null)
    if (actual && pedidoNoEcra.current === id) setPedido(actual)
  }, [])

  // Tempo real: outro terminal (ou a cozinha) alterou o pedido aberto neste ecrã.
  useEvento('pedidoAlterado', (d) => {
    void abertos.recarregar()
    if (pedido && d.pedidoId === pedido.id && !aGravar) void actualizarNoEcra(pedido.id)
  })
  useEvento('mesasAlteradas', () => void mesas.recarregar())
  useEvento('itensProntos', (d) => avisos.sucesso(`Pronto${d.mesa ? ` — mesa ${d.mesa}` : ` — ${d.pedidoNumero}`}: ${d.produtos.join(', ')}`))
  useEvento('religado', () => {
    void abertos.recarregar()
    void mesas.recarregar()
    if (pedido && !aGravar) void actualizarNoEcra(pedido.id)
  })

  /** Executa uma alteração ao pedido; em conflito (409) recarrega a versão actual. */
  const executar = async (accao: () => Promise<Pedido>, sucesso?: string) => {
    setAGravar(true)
    try {
      const actualizado = await accao()
      setPedido(actualizado)
      setRascunho(null)
      if (sucesso) avisos.sucesso(sucesso)
      return true
    } catch (e) {
      avisos.erro(mensagemErro(e))
      if (e instanceof ErroApi && e.estado === 409 && pedido) await abrirPedido(pedido.id)
      return false
    } finally {
      setAGravar(false)
      void abertos.recarregar()
    }
  }

  const novaVendaBalcao = (): Rascunho => ({ tipo: 'Balcao', chave: novaChaveIdempotencia() })
  const iniciarBalcao = () => {
    pedidoNoEcra.current = null
    setPedido(null)
    setRascunho(novaVendaBalcao())
  }

  /** Sem quantidade: produtos marcados «pede a quantidade» abrem o diálogo; os restantes lançam 1. */
  const adicionarProduto = (produto: Produto, indicada?: number) => {
    if (aGravar) return
    if (!produto.disponivel) return avisos.erro(`${produto.nome} está esgotado.`)
    if (indicada === undefined && produto.pedeQuantidade) {
      if (!(pedido?.estado === 'Aberto') && !rascunho && !opcoes.balcao) return avisos.erro('Escolha primeiro o tipo de venda.')
      return setDialogo({ tipo: 'quantidade', produto })
    }
    const quantidade = indicada ?? 1
    if (pedido && pedido.estado === 'Aberto') {
      // Mesmo produto ainda por enviar e sem observações: soma à linha existente em vez de criar outra.
      const linha = pedido.itens.find((i) => i.produtoId === produto.id && !i.cancelado && i.estadoCozinha === 'NaoEnviado' && !i.observacoes)
      if (linha && linha.quantidade + quantidade <= 999)
        return void executar(() => api.put<Pedido>(`/api/pedidos/${pedido.id}/itens/${linha.id}`, { quantidade: linha.quantidade + quantidade, observacoes: null, rowVersion: pedido.rowVersion }))
      return void executar(() => api.post<Pedido>(`/api/pedidos/${pedido.id}/itens`, { itens: [{ produtoId: produto.id, quantidade }], rowVersion: pedido.rowVersion }))
    }
    // Passar um produto no leitor sem venda iniciada começa uma venda ao balcão.
    const novo = rascunho ?? (opcoes.balcao ? novaVendaBalcao() : null)
    if (!novo) return avisos.erro('Escolha primeiro o tipo de venda.')
    void executar(() =>
      api.post<Pedido>('/api/pedidos', {
        tipo: novo.tipo,
        mesaId: novo.mesa?.id ?? null,
        numeroPessoas: 0,
        nomeEntrega: novo.nome || null,
        contactoEntrega: novo.contacto || null,
        enderecoEntrega: novo.endereco || null,
        confirmarMesaReservada: novo.confirmarReservada ?? false,
        itens: [{ produtoId: produto.id, quantidade }],
      }, { 'Idempotency-Key': novo.chave }),
    )
  }

  // ---- Leitor de código de barras: os códigos ficam em fila e tratam-se um de cada vez (leituras seguidas não se perdem).
  const filaCodigos = useRef<{ codigo: string; daPesquisa: boolean }[]>([])
  const aProcurar = useRef(false)
  const [codigosRecebidos, setCodigosRecebidos] = useState(0)
  const receberCodigo = useCallback((codigo: string, daPesquisa = false) => {
    filaCodigos.current.push({ codigo, daPesquisa })
    setCodigosRecebidos((n) => n + 1)
  }, [])

  useEffect(() => {
    if (aGravar || dialogo !== null || aProcurar.current || filaCodigos.current.length === 0) return
    const { codigo, daPesquisa } = filaCodigos.current.shift()!
    aProcurar.current = true
    api.get<Pagina<Produto>>('/api/produtos' + query({ activo: true, pesquisa: codigo, tamanho: 20 }))
      .then((r) => {
        const produto = r.itens.find((x) => x.codigoBarras === codigo || x.codigo === codigo)
        if (produto) {
          if (daPesquisa) setPesquisa('')
          adicionarProduto(produto)
        } else if (!daPesquisa) avisos.erro(`Código ${codigo} não encontrado.`)
      })
      .catch((e) => avisos.erro(mensagemErro(e)))
      .finally(() => {
        aProcurar.current = false
        setCodigosRecebidos((n) => n + 1) // passa ao código seguinte
      })
  }, [codigosRecebidos, aGravar, dialogo]) // eslint-disable-line react-hooks/exhaustive-deps

  // O leitor escreve como um teclado muito rápido e termina com Enter. Fora dos campos de texto e dos diálogos.
  const comDialogo = dialogo !== null
  useEffect(() => {
    if (!opcoes.codigoBarras || !podeGravar || comDialogo) return
    let lido = ''
    let ultima = 0
    const aoTeclar = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement | null)?.closest?.('input, textarea, select') || e.ctrlKey || e.altKey || e.metaKey) return
      const rapido = e.timeStamp - ultima < INTERVALO_LEITOR_MS
      if (e.key === 'Enter') {
        if (lido.length >= 3 && rapido) {
          e.preventDefault()
          receberCodigo(lido)
        }
        lido = ''
      } else if (e.key.length === 1) {
        lido = rapido ? lido + e.key : e.key
        ultima = e.timeStamp
      }
    }
    window.addEventListener('keydown', aoTeclar, true)
    return () => window.removeEventListener('keydown', aoTeclar, true)
  }, [opcoes.codigoBarras, podeGravar, comDialogo, receberCodigo])

  const p = pedido
  const comPedido = <T,>(fn: (pedido: Pedido) => Promise<T>) => (p ? fn(p) : Promise.reject(new Error('Sem pedido')))

  const cabecalho = p
    ? nomeTipo(p.tipo, p.mesaNome)
    : rascunho
      ? `${nomeTipo(rascunho.tipo, rascunho.mesa?.nome)} (novo)`
      : 'Sem pedido'

  /**
   * Recibo automático feito por este posto quando o servidor não tem impressora de caixa para o terminal
   * (ex.: impressora de talão USB ligada ao próprio posto). Com impressora no servidor, é ele que imprime.
   */
  const imprimirReciboNoPosto = async (pago: Pedido) => {
    try {
      const destino = await api.get<{ impressoraServidor: string | null; automatico: boolean }>('/api/pedidos/recibo/destino' + query({ terminalId: lerTerminalLocal() }))
      if (!destino.automatico || destino.impressoraServidor) return
      imprimirNoNavegador(await api.get<DocumentoImpressao>(`/api/pedidos/${pago.id}/recibo/documento`), 80, publicas.dados, 'venda-dinheiro')
    } catch (e) {
      avisos.erro(mensagemErro(e, 'O pagamento ficou registado, mas não foi possível imprimir o recibo neste posto.'))
    }
  }

  const imprimir = async () => {
    if (!p) return
    try {
      // Impressora de caixa configurada no servidor (deste terminal ou a geral): imprime sem diálogo.
      const r = await api.post<{ impressora: string; pdf: boolean }>(`/api/pedidos/${p.id}/recibo/imprimir`, { terminalId: lerTerminalLocal() })
      avisos.sucesso(r.pdf ? 'O recibo abre em PDF dentro de momentos.' : `Recibo enviado para a impressora ${r.impressora}.`)
      return
    } catch (e) {
      if (!(e instanceof ErroApi && e.estado === 422)) {
        avisos.erro(mensagemErro(e, 'Não foi possível imprimir o recibo.'))
        return
      }
    }
    // Sem impressora de caixa: janela de impressão do navegador neste posto (talão de 80 mm, com o logótipo).
    try {
      imprimirNoNavegador(await api.get<DocumentoImpressao>(`/api/pedidos/${p.id}/recibo/documento`), 80, publicas.dados, 'venda-dinheiro')
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível imprimir o recibo.'))
    }
  }

  /** Conta para o cliente: na impressora de caixa do terminal (directa ou PDF) ou, sem ela, pela janela de impressão deste posto. */
  const pedirConta = async () => {
    if (!p) return
    try {
      const r = await api.post<{ impressora: string | null; pdf: boolean; documento: DocumentoImpressao }>(`/api/pedidos/${p.id}/conta`, { terminalId: lerTerminalLocal() })
      if (r.impressora) avisos.sucesso(r.pdf ? 'A conta abre em PDF dentro de momentos.' : `Conta enviada para a impressora ${r.impressora}.`)
      else imprimirNoNavegador(r.documento, 80, publicas.dados, 'conta')
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível imprimir a conta.'))
    }
  }

  const reimprimirTaloes = async () => {
    if (!p) return
    try {
      const r = await api.post<{ itens: number }>(`/api/pedidos/${p.id}/taloes/reimprimir`)
      avisos.sucesso(`Talões reenviados para a produção (${r.itens} ${r.itens === 1 ? 'item' : 'itens'}).`)
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível reimprimir os talões.'))
    }
  }

  const listaProdutos = produtos.dados?.itens ?? []
  // Categorias principais nos separadores; as subcategorias da escolhida aparecem por baixo.
  const principaisPos = categorias.dados?.filter((c) => !c.categoriaPaiId) ?? []
  const seleccionada = categorias.dados?.find((c) => c.id === categoriaId)
  const principalSeleccionada = seleccionada ? seleccionada.categoriaPaiId ?? seleccionada.id : null
  const subcategoriasPos = categorias.dados?.filter((c) => c.categoriaPaiId === principalSeleccionada) ?? []
  const corCategoria = (id: number) => categorias.dados?.find((c) => c.id === id)?.cor ?? '#2563EB'
  // A venda só começa depois de escolhido o tipo (mesa, Take Away, Delivery ou Balcão).
  const modo: TipoPedido | null = p?.tipo ?? rascunho?.tipo ?? null
  const classeModo = (t: TipoPedido) => (modo === t ? 'btn-primario' : 'btn-secundario')

  return (
    <div className="-m-4 flex h-[calc(100vh-4rem)] flex-col gap-3 p-3 lg:-m-6 lg:flex-row">
      {/* Área de produtos */}
      {/* min-w-0: com muitas categorias, a barra de categorias desliza em vez de alargar a página. */}
      <section className="flex min-h-0 min-w-0 flex-1 flex-col gap-3" aria-label="Produtos">
        <div className="flex flex-wrap items-center gap-2">
          {opcoes.balcao && (
            <button type="button" className={classeModo('Balcao')} aria-pressed={modo === 'Balcao'} onClick={iniciarBalcao} disabled={!podeGravar}>
              <Store className="h-4 w-4" aria-hidden /> Balcão
            </button>
          )}
          {opcoes.mesa && (
            <button type="button" className={classeModo('Mesa')} aria-pressed={modo === 'Mesa'} onClick={() => setDialogo({ tipo: 'mesa' })} disabled={!podeGravar}>
              <LayoutGrid className="h-4 w-4" aria-hidden /> Mesa
            </button>
          )}
          {opcoes.takeAway && (
            <button type="button" className={classeModo('TakeAway')} aria-pressed={modo === 'TakeAway'} onClick={() => setDialogo({ tipo: 'entrega', pedido: 'TakeAway' })} disabled={!podeGravar}>
              <ShoppingBag className="h-4 w-4" aria-hidden /> Take Away
            </button>
          )}
          {opcoes.delivery && (
            <button type="button" className={classeModo('Delivery')} aria-pressed={modo === 'Delivery'} onClick={() => setDialogo({ tipo: 'entrega', pedido: 'Delivery' })} disabled={!podeGravar}>
              <Truck className="h-4 w-4" aria-hidden /> Delivery
            </button>
          )}
          <button type="button" className="btn-secundario" onClick={() => setDialogo({ tipo: 'abertos' })}>
            <ClipboardList className="h-4 w-4" aria-hidden /> Abertos ({abertos.dados?.total ?? 0})
          </button>
          {modo && (
            <div className="relative ml-auto min-w-48 flex-1 sm:max-w-xs">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
              <input type="search" className="campo pl-9" placeholder="Pesquisar produto ou código…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar produto"
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && opcoes.codigoBarras && pesquisa.trim() && !pesquisa.includes(' ')) receberCodigo(pesquisa.trim(), true)
                }} />
            </div>
          )}
        </div>

        {!modo ? (
          <InicioVenda mesas={mesas.dados} erroMesas={mesas.erro} podeGravar={podeGravar} opcoes={opcoes}
            aoBalcao={iniciarBalcao}
            aoEscolherMesa={(m) => void escolherMesa(m)}
            aoEscolherEntrega={(t) => setDialogo({ tipo: 'entrega', pedido: t })} />
        ) : (<>
        <div className="flex gap-2 overflow-x-auto pb-1" role="tablist" aria-label="Categorias">
          <button type="button" role="tab" aria-selected={categoriaId === null} onClick={() => setCategoriaId(null)}
            className={`min-h-11 shrink-0 rounded-full px-4 text-sm font-medium ${categoriaId === null ? 'bg-primaria-600 text-white' : 'bg-white ring-1 ring-slate-300'}`}>
            Todas
          </button>
          {principaisPos.map((c) => (
            <button key={c.id} type="button" role="tab" aria-selected={principalSeleccionada === c.id} onClick={() => setCategoriaId(c.id)}
              className={`min-h-11 shrink-0 rounded-full px-4 text-sm font-medium ${principalSeleccionada === c.id ? 'text-white' : 'bg-white ring-1 ring-slate-300'}`}
              style={principalSeleccionada === c.id ? { backgroundColor: c.cor ?? '#2563EB' } : undefined}>
              {c.nome}
            </button>
          ))}
        </div>
        {subcategoriasPos.length > 0 && principalSeleccionada !== null && (
          <div className="-mt-1 flex gap-2 overflow-x-auto pb-1" role="tablist" aria-label="Subcategorias">
            <button type="button" role="tab" aria-selected={categoriaId === principalSeleccionada} onClick={() => setCategoriaId(principalSeleccionada)}
              className={`min-h-9 shrink-0 rounded-full px-3 text-xs font-medium ${categoriaId === principalSeleccionada ? 'bg-slate-800 text-white' : 'bg-white ring-1 ring-slate-300'}`}>
              Todas
            </button>
            {subcategoriasPos.map((c) => (
              <button key={c.id} type="button" role="tab" aria-selected={categoriaId === c.id} onClick={() => setCategoriaId(c.id)}
                className={`min-h-9 shrink-0 rounded-full px-3 text-xs font-medium ${categoriaId === c.id ? 'text-white' : 'bg-white ring-1 ring-slate-300'}`}
                style={categoriaId === c.id ? { backgroundColor: c.cor ?? '#2563EB' } : undefined}>
                {c.nome}
              </button>
            ))}
          </div>
        )}

        {produtos.erro && <Alerta tipo="erro">{produtos.erro}</Alerta>}
        <div className="grid min-h-0 flex-1 auto-rows-min grid-cols-2 gap-2 overflow-y-auto sm:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
          {listaProdutos.length === 0 && !produtos.aCarregar && (
            <div className="col-span-full"><Vazio texto="Sem produtos activos nesta categoria." /></div>
          )}
          {listaProdutos.map((pr) => (
            <button key={pr.id} type="button" onClick={() => adicionarProduto(pr)} disabled={aGravar || !podeGravar || !pr.disponivel}
              className="relative flex min-h-24 flex-col justify-between rounded-xl border-l-8 bg-white p-3 text-left shadow-sm ring-1 ring-slate-200 transition hover:shadow active:scale-[0.98] disabled:opacity-50"
              style={{ borderLeftColor: corCategoria(pr.categoriaId) }}>
              {pr.imagemUrl && <img src={pr.imagemUrl} alt="" loading="lazy" className="mb-2 h-20 w-full rounded-md object-cover" />}
              <span className="font-medium leading-tight">{pr.nome}</span>
              <span className="mt-2 font-semibold text-primaria-700 tabular-nums">{formatarMoeda(pr.precoVenda)}</span>
              {!pr.disponivel && <span className="absolute right-2 top-2"><Etiqueta cor="amarelo">Esgotado</Etiqueta></span>}
            </button>
          ))}
        </div>
        </>)}
      </section>

      {/* Pedido actual */}
      <div className="relative min-h-[50vh] w-full lg:min-h-0 lg:w-96">
        {aGravar && <Loader2 className="absolute right-14 top-4 h-5 w-5 animate-spin text-primaria-600" aria-label="A gravar" />}
        <PainelPedido
          pedido={p}
          cabecalho={cabecalho}
          aGravar={aGravar}
          podeGravar={podeGravar}
          permissoes={{
            desconto: tem('pos.desconto'),
            cancelarItem: tem('pos.cancelar-item'),
            cancelarPedido: tem('pos.cancelar-pedido'),
            transferir: tem('pos.transferir-mesa'),
            pagar: tem('pagamentos.registar'),
            estornar: tem('pagamentos.anular'),
          }}
          accoes={{
            alterarQuantidade: (item, quantidade) => void executar(() => comPedido((x) =>
              api.put<Pedido>(`/api/pedidos/${x.id}/itens/${item.id}`, { quantidade, observacoes: item.observacoes, rowVersion: x.rowVersion }))),
            remover: (item) => void executar(() => comPedido((x) => api.post<Pedido>(`/api/pedidos/${x.id}/itens/${item.id}/remover`, { rowVersion: x.rowVersion }))),
            observacoes: (item) => setDialogo({ tipo: 'observacoes', item }),
            cancelarItem: (item) => setDialogo({ tipo: 'cancelar-item', item }),
            enviarCozinha: () => void executar(() => comPedido((x) => api.post<Pedido>(`/api/pedidos/${x.id}/enviar-cozinha`, { rowVersion: x.rowVersion })), 'Pedido enviado.'),
            desconto: () => setDialogo({ tipo: 'desconto' }),
            transferir: () => setDialogo({ tipo: 'transferir' }),
            pagar: () => setDialogo({ tipo: 'pagamento' }),
            cancelarPedido: () => setDialogo({ tipo: 'cancelar-pedido' }),
            imprimirRecibo: () => void imprimir(),
            reimprimirTaloes: () => void reimprimirTaloes(),
            pedirConta: () => void pedirConta(),
            estornar: () => setDialogo({ tipo: 'estorno' }),
            cliente: () => setDialogo({ tipo: 'cliente' }),
            fechar: () => { setPedido(null); setRascunho(null) },
          }}
        />
      </div>

      {dialogo?.tipo === 'mesa' && mesas.dados && (
        <DialogoMesa titulo="Escolher mesa" mesas={mesas.dados} aoFechar={() => setDialogo(null)} aoEscolher={(m) => void escolherMesa(m)} />
      )}
      {dialogo?.tipo === 'reservada' && (
        <Modal titulo={`Mesa ${dialogo.mesa.nome} reservada`} aoFechar={() => setDialogo(null)}
          rodape={<><button type="button" className="btn-secundario" onClick={() => setDialogo(null)}>Cancelar</button>
            <button type="button" className="btn-primario" onClick={() => void escolherMesa(dialogo.mesa, true)}>Ocupar mesmo assim</button></>}>
          <p className="text-sm">Esta mesa está reservada. Confirme que pretende ocupá-la com um novo pedido.</p>
        </Modal>
      )}
      {dialogo?.tipo === 'transferir' && mesas.dados && p && (
        <DialogoMesa titulo="Transferir para a mesa…" apenasLivres mesas={mesas.dados.filter((m) => m.id !== p.mesaId)} aoFechar={() => setDialogo(null)}
          aoEscolher={(m) => { setDialogo(null); void executar(() => api.post<Pedido>(`/api/pedidos/${p.id}/transferir`, { mesaDestinoId: m.id, motivo: null, rowVersion: p.rowVersion }), `Pedido transferido para a mesa ${m.nome}.`) }} />
      )}
      {dialogo?.tipo === 'entrega' && (
        <DialogoEntrega tipo={dialogo.pedido} aoFechar={() => setDialogo(null)}
          aoConfirmar={(d) => { setPedido(null); setRascunho({ tipo: dialogo.pedido, nome: d.nome, contacto: d.contacto, endereco: d.endereco, chave: novaChaveIdempotencia() }); setDialogo(null) }} />
      )}
      {dialogo?.tipo === 'quantidade' && (
        <DialogoQuantidade produto={dialogo.produto.nome} unidade={dialogo.produto.unidade} aoFechar={() => setDialogo(null)}
          aoConfirmar={(q) => { setDialogo(null); adicionarProduto(dialogo.produto, q) }} />
      )}
      {dialogo?.tipo === 'observacoes' && p && (
        <DialogoObservacoes produto={dialogo.item.nomeProduto} inicial={dialogo.item.observacoes ?? ''} aoFechar={() => setDialogo(null)}
          aoConfirmar={async (texto) => {
            if (await executar(() => api.put<Pedido>(`/api/pedidos/${p.id}/itens/${dialogo.item.id}`, { quantidade: dialogo.item.quantidade, observacoes: texto || null, rowVersion: p.rowVersion })))
              setDialogo(null)
          }} />
      )}
      {dialogo?.tipo === 'cancelar-item' && p && (
        <DialogoMotivo titulo={`Cancelar ${dialogo.item.nomeProduto}`} texto="Cancelar item" aoFechar={() => setDialogo(null)}
          descricao="O item já foi enviado. O cancelamento fica registado com o seu nome e o motivo, e a cozinha é avisada."
          aoConfirmar={async (motivo) => {
            if (await executar(() => api.post<Pedido>(`/api/pedidos/${p.id}/itens/${dialogo.item.id}/cancelar`, { motivo, rowVersion: p.rowVersion }), 'Item cancelado.'))
              setDialogo(null)
          }} />
      )}
      {dialogo?.tipo === 'cancelar-pedido' && p && (
        <DialogoMotivo titulo={`Cancelar o pedido ${p.numero}`} texto="Cancelar pedido" aoFechar={() => setDialogo(null)}
          descricao="Todos os itens são cancelados e a mesa é libertada. Se houver pagamentos, terão de ser anulados primeiro."
          aoConfirmar={async (motivo) => {
            if (await executar(() => api.post<Pedido>(`/api/pedidos/${p.id}/cancelar`, { motivo, rowVersion: p.rowVersion }), 'Pedido cancelado.'))
              setDialogo(null)
          }} />
      )}
      {dialogo?.tipo === 'desconto' && p && (
        <DialogoDesconto pedido={p} aoFechar={() => setDialogo(null)}
          aoConfirmar={async (d) => {
            if (await executar(() => api.post<Pedido>(`/api/pedidos/${p.id}/desconto`, { ...d, rowVersion: p.rowVersion }), 'Desconto aplicado.'))
              setDialogo(null)
          }} />
      )}
      {dialogo?.tipo === 'pagamento' && p && (
        <ModalPagamento pedido={p} aoFechar={() => setDialogo(null)}
          aoPagar={(actualizado) => {
            setPedido(actualizado)
            setDialogo(null)
            void abertos.recarregar()
            if (actualizado.estado === 'Pago') {
              avisos.sucesso(`Pedido ${actualizado.numero} pago. ${documentoVenda(actualizado.reciboNumero)}.`)
              void imprimirReciboNoPosto(actualizado)
              // Balcão: o ecrã fica logo pronto para a venda seguinte.
              if (actualizado.tipo === 'Balcao') iniciarBalcao()
            }
            else avisos.sucesso(`Pagamento registado. Em dívida: ${formatarMoeda(actualizado.emDivida)}.`)
          }} />
      )}
      {dialogo?.tipo === 'cliente' && p && (
        <DialogoCliente pedido={p} aoFechar={() => setDialogo(null)}
          aoEscolher={(clienteId) => {
            setDialogo(null)
            void executar(() => api.post<Pedido>(`/api/pedidos/${p.id}/cliente`, { clienteId, rowVersion: p.rowVersion }), clienteId ? 'Cliente associado.' : 'Cliente retirado.')
          }} />
      )}
      {dialogo?.tipo === 'estorno' && p && (
        <DialogoEstorno pedido={p} aoFechar={() => setDialogo(null)}
          aoEstornar={(actualizado) => { setPedido(actualizado); setDialogo(null); avisos.sucesso(`Pedido ${actualizado.numero} estornado.`) }} />
      )}
      {dialogo?.tipo === 'abertos' && (
        <ListaPedidos abertos={abertos.dados?.itens ?? []} aoFechar={() => setDialogo(null)} aoAbrir={(id) => { setDialogo(null); void abrirPedido(id) }} />
      )}
    </div>
  )
}

/** Início da venda: Balcão, a mesa (directamente no mapa) ou um pedido Take Away / Delivery, conforme as configurações. */
function InicioVenda({ mesas, erroMesas, podeGravar, opcoes, aoBalcao, aoEscolherMesa, aoEscolherEntrega }: {
  mesas: Mesa[] | null
  erroMesas: string | null
  podeGravar: boolean
  opcoes: OpcoesPos
  aoBalcao: () => void
  aoEscolherMesa: (mesa: Mesa) => void
  aoEscolherEntrega: (tipo: 'TakeAway' | 'Delivery') => void
}) {
  return (
    <div className="min-h-0 flex-1 space-y-4 overflow-y-auto rounded-xl bg-white p-4 ring-1 ring-slate-200">
      <div>
        <h2 className="text-lg font-semibold">Iniciar venda</h2>
        <p className="text-sm text-slate-600">Escolha o tipo de venda para começar a lançar produtos.{opcoes.codigoBarras && opcoes.balcao && ' Também pode passar um produto no leitor de código de barras.'}</p>
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        {opcoes.balcao && (
          <button type="button" className="flex min-h-20 items-center gap-3 rounded-xl border-2 border-slate-200 p-4 text-left hover:border-primaria-500 disabled:opacity-50"
            onClick={aoBalcao} disabled={!podeGravar}>
            <Store className="h-8 w-8 text-primaria-600" aria-hidden />
            <span><span className="block font-semibold">Balcão</span><span className="text-sm text-slate-600">Venda rápida</span></span>
          </button>
        )}
        {opcoes.takeAway && (
          <button type="button" className="flex min-h-20 items-center gap-3 rounded-xl border-2 border-slate-200 p-4 text-left hover:border-primaria-500 disabled:opacity-50"
            onClick={() => aoEscolherEntrega('TakeAway')} disabled={!podeGravar}>
            <ShoppingBag className="h-8 w-8 text-primaria-600" aria-hidden />
            <span><span className="block font-semibold">Take Away</span><span className="text-sm text-slate-600">Para levar</span></span>
          </button>
        )}
        {opcoes.delivery && (
          <button type="button" className="flex min-h-20 items-center gap-3 rounded-xl border-2 border-slate-200 p-4 text-left hover:border-primaria-500 disabled:opacity-50"
            onClick={() => aoEscolherEntrega('Delivery')} disabled={!podeGravar}>
            <Truck className="h-8 w-8 text-primaria-600" aria-hidden />
            <span><span className="block font-semibold">Delivery</span><span className="text-sm text-slate-600">Entrega com nome, contacto e endereço</span></span>
          </button>
        )}
      </div>
      {opcoes.mesa && (
        <div>
          <h3 className="mb-2 font-semibold">Mesas</h3>
          {erroMesas && <Alerta tipo="erro">{erroMesas}</Alerta>}
          {!mesas && !erroMesas && <Loader2 className="h-5 w-5 animate-spin text-primaria-600" aria-label="A carregar mesas" />}
          {mesas && <GrelhaMesas mesas={mesas} aoEscolher={aoEscolherMesa} desactivado={!podeGravar} />}
        </div>
      )}
    </div>
  )
}

/** Pedidos abertos e recentes (para reimprimir recibos ou estornar). */
function ListaPedidos({ abertos, aoAbrir, aoFechar }: { abertos: PedidoResumo[]; aoAbrir: (id: number) => void; aoFechar: () => void }) {
  const [separador, setSeparador] = useState<'abertos' | 'recentes'>('abertos')
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const recentes = useDados(
    () => (separador === 'recentes' ? api.get<Pagina<PedidoResumo>>('/api/pedidos' + query({ pesquisa: pesquisaAtrasada, tamanho: 50 })) : Promise.resolve(null)),
    [separador, pesquisaAtrasada],
  )
  const lista = separador === 'abertos' ? abertos : (recentes.dados?.itens ?? [])
  const cor = { Aberto: 'azul', Pago: 'verde', Cancelado: 'cinza', Estornado: 'vermelho' } as const

  return (
    <Modal titulo="Pedidos" aoFechar={aoFechar} largura="max-w-2xl">
      <div className="mb-3 flex flex-wrap gap-2">
        {(['abertos', 'recentes'] as const).map((s) => (
          <button key={s} type="button" onClick={() => setSeparador(s)} className={separador === s ? 'btn-primario' : 'btn-secundario'}>
            {s === 'abertos' ? `Abertos (${abertos.length})` : 'Recentes'}
          </button>
        ))}
        {separador === 'recentes' && (
          <input type="search" className="campo flex-1" placeholder="N.º do pedido, nome, contacto ou mesa…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar pedidos" />
        )}
      </div>
      {lista.length === 0 && <Vazio texto={separador === 'abertos' ? 'Não há pedidos abertos.' : 'Sem pedidos.'} />}
      <ul className="divide-y divide-slate-100">
        {lista.map((a) => (
              <li key={a.id}>
                <button type="button" className="flex min-h-14 w-full items-center gap-3 px-2 text-left hover:bg-slate-50" onClick={() => aoAbrir(a.id)}>
                  <span className="w-28 font-semibold">{nomeTipo(a.tipo, a.mesaNome)}</span>
                  <span className="flex-1 text-sm text-slate-600">{a.numero}{a.nomeEntrega && ` · ${a.nomeEntrega}`}{a.contactoEntrega && ` · ${a.contactoEntrega}`}</span>
                  {a.estado !== 'Aberto' && <Etiqueta cor={cor[a.estado]}>{a.estado}</Etiqueta>}
                  {a.estado === 'Aberto' && a.itensPorEnviar > 0 && <Etiqueta cor="cinza">{a.itensPorEnviar} por enviar</Etiqueta>}
                  {a.estado === 'Aberto' && a.itensProntos > 0 && <Etiqueta cor="verde">{a.itensProntos} prontos</Etiqueta>}
                  <span className="w-28 text-right font-semibold tabular-nums">{formatarMoeda(a.total)}</span>
                </button>
              </li>
        ))}
      </ul>
    </Modal>
  )
}
