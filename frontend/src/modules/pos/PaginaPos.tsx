import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ClipboardList, LayoutGrid, Loader2, Search, ShoppingBag, Truck } from 'lucide-react'
import { api, ErroApi, novaChaveIdempotencia, query } from '../../api/cliente'
import { Alerta, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useEvento } from '../../contexts/TempoRealContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Categoria, Mesa, Pagina, Pedido, PedidoItem, PedidoResumo, Produto, Recibo, TipoPedido } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { DialogoDesconto, DialogoEntrega, DialogoMesa, DialogoMotivo, DialogoObservacoes } from './Dialogos'
import { DialogoCliente } from './DialogoCliente'
import { DialogoEstorno } from './DialogoEstorno'
import { ModalPagamento } from './ModalPagamento'
import { PainelPedido } from './PainelPedido'
import { imprimirRecibo } from './recibo'
import { lerTerminalLocal } from '../../utils/terminalLocal'

/** Pedido novo ainda sem itens: só existe no servidor quando se adiciona o primeiro produto. */
interface Rascunho {
  tipo: TipoPedido
  mesa?: Mesa
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
  const produtos = useDados(
    () => api.get<Pagina<Produto>>('/api/produtos' + query({ activo: true, categoriaId, pesquisa: pesquisaAtrasada, tamanho: 200 })),
    [categoriaId, pesquisaAtrasada],
  )
  const mesas = useDados(() => api.get<Mesa[]>('/api/mesas'), [])
  const abertos = useDados(() => api.get<Pagina<PedidoResumo>>('/api/pedidos?estado=Aberto&tamanho=100'), [])

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

  // Entrada por URL: /pos?mesa=ID (a partir do mapa de mesas) ou /pos?pedido=ID.
  useEffect(() => {
    const idMesa = Number(parametros.get('mesa'))
    const idPedido = Number(parametros.get('pedido'))
    if (idPedido) void abrirPedido(idPedido)
    else if (idMesa) api.get<Mesa>(`/api/mesas/${idMesa}`).then((m) => void escolherMesa(m)).catch((e) => avisos.erro(mensagemErro(e)))
    if (idMesa || idPedido) setParametros({}, { replace: true })
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // Tempo real: outro terminal (ou a cozinha) alterou o pedido aberto neste ecrã.
  useEvento('pedidoAlterado', (d) => {
    void abertos.recarregar()
    if (pedido && d.pedidoId === pedido.id && !aGravar) void abrirPedido(pedido.id)
  })
  useEvento('mesasAlteradas', () => void mesas.recarregar())
  useEvento('itensProntos', (d) => avisos.sucesso(`Pronto${d.mesa ? ` — mesa ${d.mesa}` : ` — ${d.pedidoNumero}`}: ${d.produtos.join(', ')}`))
  useEvento('religado', () => {
    void abertos.recarregar()
    void mesas.recarregar()
    if (pedido && !aGravar) void abrirPedido(pedido.id)
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

  const adicionarProduto = (produto: Produto) => {
    if (aGravar) return
    if (!produto.disponivel) return avisos.erro(`${produto.nome} está esgotado.`)
    if (pedido && pedido.estado === 'Aberto') {
      // Mesmo produto ainda por enviar e sem observações: soma à linha existente em vez de criar outra.
      const linha = pedido.itens.find((i) => i.produtoId === produto.id && !i.cancelado && i.estadoCozinha === 'NaoEnviado' && !i.observacoes)
      if (linha && linha.quantidade + 1 <= 999)
        return void executar(() => api.put<Pedido>(`/api/pedidos/${pedido.id}/itens/${linha.id}`, { quantidade: linha.quantidade + 1, observacoes: null, rowVersion: pedido.rowVersion }))
      return void executar(() => api.post<Pedido>(`/api/pedidos/${pedido.id}/itens`, { itens: [{ produtoId: produto.id, quantidade: 1 }], rowVersion: pedido.rowVersion }))
    }
    if (!rascunho) return avisos.erro('Escolha primeiro a mesa, Take Away ou Delivery.')
    void executar(() =>
      api.post<Pedido>('/api/pedidos', {
        tipo: rascunho.tipo,
        mesaId: rascunho.mesa?.id ?? null,
        numeroPessoas: 0,
        contactoEntrega: rascunho.contacto || null,
        enderecoEntrega: rascunho.endereco || null,
        confirmarMesaReservada: rascunho.confirmarReservada ?? false,
        itens: [{ produtoId: produto.id, quantidade: 1 }],
      }, { 'Idempotency-Key': rascunho.chave }),
    )
  }

  const p = pedido
  const comPedido = <T,>(fn: (pedido: Pedido) => Promise<T>) => (p ? fn(p) : Promise.reject(new Error('Sem pedido')))

  const cabecalho = p
    ? p.tipo === 'Mesa' ? `Mesa ${p.mesaNome}` : p.tipo === 'TakeAway' ? 'Take Away' : 'Delivery'
    : rascunho
      ? rascunho.tipo === 'Mesa' ? `Mesa ${rascunho.mesa?.nome} (novo)` : rascunho.tipo === 'TakeAway' ? 'Take Away (novo)' : 'Delivery (novo)'
      : 'Sem pedido'

  const imprimir = async () => {
    if (!p) return
    try {
      // Impressora de caixa configurada no servidor (deste terminal ou a geral): imprime sem diálogo.
      const r = await api.post<{ impressora: string }>(`/api/pedidos/${p.id}/recibo/imprimir`, { terminalId: lerTerminalLocal() })
      avisos.sucesso(`Recibo enviado para a impressora ${r.impressora}.`)
      return
    } catch (e) {
      if (!(e instanceof ErroApi && e.estado === 422)) {
        avisos.erro(mensagemErro(e, 'Não foi possível imprimir o recibo.'))
        return
      }
    }
    // Sem impressora de caixa: janela de impressão do navegador neste posto.
    try {
      imprimirRecibo(await api.get<Recibo>(`/api/pedidos/${p.id}/recibo`))
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível imprimir o recibo.'))
    }
  }

  const listaProdutos = produtos.dados?.itens ?? []
  // Categorias principais nos separadores; as subcategorias da escolhida aparecem por baixo.
  const principaisPos = categorias.dados?.filter((c) => !c.categoriaPaiId) ?? []
  const seleccionada = categorias.dados?.find((c) => c.id === categoriaId)
  const principalSeleccionada = seleccionada ? seleccionada.categoriaPaiId ?? seleccionada.id : null
  const subcategoriasPos = categorias.dados?.filter((c) => c.categoriaPaiId === principalSeleccionada) ?? []
  const corCategoria = (id: number) => categorias.dados?.find((c) => c.id === id)?.cor ?? '#2563EB'

  return (
    <div className="-m-4 flex h-[calc(100vh-4rem)] flex-col gap-3 p-3 lg:-m-6 lg:flex-row">
      {/* Área de produtos */}
      {/* min-w-0: com muitas categorias, a barra de categorias desliza em vez de alargar a página. */}
      <section className="flex min-h-0 min-w-0 flex-1 flex-col gap-3" aria-label="Produtos">
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" className="btn-secundario" onClick={() => setDialogo({ tipo: 'mesa' })} disabled={!podeGravar}>
            <LayoutGrid className="h-4 w-4" aria-hidden /> Mesa
          </button>
          <button type="button" className="btn-secundario" onClick={() => setDialogo({ tipo: 'entrega', pedido: 'TakeAway' })} disabled={!podeGravar}>
            <ShoppingBag className="h-4 w-4" aria-hidden /> Take Away
          </button>
          <button type="button" className="btn-secundario" onClick={() => setDialogo({ tipo: 'entrega', pedido: 'Delivery' })} disabled={!podeGravar}>
            <Truck className="h-4 w-4" aria-hidden /> Delivery
          </button>
          <button type="button" className="btn-secundario" onClick={() => setDialogo({ tipo: 'abertos' })}>
            <ClipboardList className="h-4 w-4" aria-hidden /> Abertos ({abertos.dados?.total ?? 0})
          </button>
          <div className="relative ml-auto min-w-48 flex-1 sm:max-w-xs">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
            <input type="search" className="campo pl-9" placeholder="Pesquisar produto ou código…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar produto" />
          </div>
        </div>

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
          aoConfirmar={(d) => { setPedido(null); setRascunho({ tipo: dialogo.pedido, contacto: d.contacto, endereco: d.endereco, chave: novaChaveIdempotencia() }); setDialogo(null) }} />
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
            if (actualizado.estado === 'Pago') avisos.sucesso(`Pedido ${actualizado.numero} pago. Recibo ${actualizado.reciboNumero}.`)
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
          <input type="search" className="campo flex-1" placeholder="N.º do pedido, contacto ou mesa…" value={pesquisa} onChange={(e) => setPesquisa(e.target.value)} aria-label="Pesquisar pedidos" />
        )}
      </div>
      {lista.length === 0 && <Vazio texto={separador === 'abertos' ? 'Não há pedidos abertos.' : 'Sem pedidos.'} />}
      <ul className="divide-y divide-slate-100">
        {lista.map((a) => (
              <li key={a.id}>
                <button type="button" className="flex min-h-14 w-full items-center gap-3 px-2 text-left hover:bg-slate-50" onClick={() => aoAbrir(a.id)}>
                  <span className="w-28 font-semibold">{a.tipo === 'Mesa' ? `Mesa ${a.mesaNome}` : a.tipo === 'TakeAway' ? 'Take Away' : 'Delivery'}</span>
                  <span className="flex-1 text-sm text-slate-600">{a.numero}{a.contactoEntrega && ` · ${a.contactoEntrega}`}</span>
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
