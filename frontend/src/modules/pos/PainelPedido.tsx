import { useEffect, useRef, useState, type ReactNode } from 'react'
import { ArrowRightLeft, ChefHat, Ban, Ellipsis, MessageSquare, Minus, Percent, Plus, Printer, ReceiptText, Trash2, Undo2, UserCheck, UserRound, Wallet, X } from 'lucide-react'
import { Etiqueta } from '../../components/ui'
import type { EstadoItemCozinha, Pedido, PedidoItem } from '../../types/api'
import { documentoVenda, formatarMoeda } from '../../utils/formatar'

/** Quantidade com vírgula decimal (ex.: 0,485 kg). */
const quantidade = (q: number) => q.toLocaleString('pt-PT', { maximumFractionDigits: 3 })

const ESTADO_ITEM: Record<EstadoItemCozinha, { texto: string; cor: 'cinza' | 'amarelo' | 'azul' | 'verde' }> = {
  NaoEnviado: { texto: 'Por enviar', cor: 'cinza' },
  Pendente: { texto: 'Pendente', cor: 'amarelo' },
  EmPreparacao: { texto: 'Em preparação', cor: 'azul' },
  Pronto: { texto: 'Pronto', cor: 'verde' },
  Entregue: { texto: 'Entregue', cor: 'verde' },
}

export interface AccoesPedido {
  alterarQuantidade: (item: PedidoItem, quantidade: number) => void
  remover: (item: PedidoItem) => void
  observacoes: (item: PedidoItem) => void
  comentarioPedido: () => void
  cancelarItem: (item: PedidoItem) => void
  enviarCozinha: () => void
  desconto: () => void
  transferir: () => void
  passarMesa: () => void
  pagar: () => void
  cancelarPedido: () => void
  imprimirRecibo: () => void
  reimprimirTaloes: () => void
  pedirConta: () => void
  estornar: () => void
  cliente: () => void
  fechar: () => void
}

export function PainelPedido({ pedido, cabecalho, aGravar, podeGravar, permissoes, accoes }: {
  pedido: Pedido | null
  cabecalho: string
  aGravar: boolean
  podeGravar: boolean
  permissoes: { desconto: boolean; cancelarItem: boolean; cancelarPedido: boolean; transferir: boolean; passarMesa: boolean; pagar: boolean; estornar: boolean }
  accoes: AccoesPedido
}) {
  // Itens retirados antes do envio não interessam ao operador; os cancelados depois do envio mostram-se riscados.
  const itens = pedido?.itens.filter((i) => !(i.cancelado && i.estadoCozinha === 'NaoEnviado')) ?? []
  const porEnviar = itens.filter((i) => !i.cancelado && i.estadoCozinha === 'NaoEnviado').length
  const enviados = itens.filter((i) => !i.cancelado && i.estadoCozinha !== 'NaoEnviado').length
  const aberto = pedido?.estado === 'Aberto'
  const bloqueado = aGravar || !podeGravar
  const mesa = pedido?.tipo === 'Mesa'

  // Opções menos usadas: no menu «⋯» do topo, para a lista do pedido ter mais espaço (ecrãs de 1024x768).
  const opcoesMenu: (OpcaoMenu | null)[] = pedido && aberto ? [
    { texto: pedido.clienteNome ? 'Mudar o cliente' : 'Associar cliente (fidelização)', icone: <UserRound className="h-4 w-4" />, accao: accoes.cliente, desactivada: bloqueado },
    { texto: 'Comentário do pedido', icone: <MessageSquare className="h-4 w-4" />, accao: accoes.comentarioPedido, desactivada: bloqueado },
    null,
    enviados > 0 ? { texto: 'Reimprimir talões', icone: <Printer className="h-4 w-4" />, accao: accoes.reimprimirTaloes, desactivada: bloqueado,
      dica: 'Volta a imprimir nos centros de produção o que já foi enviado' } : null,
    permissoes.desconto ? { texto: 'Desconto', icone: <Percent className="h-4 w-4" />, accao: accoes.desconto, desactivada: bloqueado || pedido.subtotal === 0 } : null,
    permissoes.transferir && mesa ? { texto: 'Transferir', icone: <ArrowRightLeft className="h-4 w-4" />, accao: accoes.transferir, desactivada: bloqueado } : null,
    permissoes.passarMesa && mesa ? { texto: 'Passar mesa', icone: <UserCheck className="h-4 w-4" />, accao: accoes.passarMesa, desactivada: bloqueado,
      dica: 'Passar a mesa a outro responsável (ex.: mudança de turno)' } : null,
    null,
    permissoes.cancelarPedido ? { texto: 'Cancelar pedido', icone: <Ban className="h-4 w-4" />, accao: accoes.cancelarPedido, desactivada: bloqueado, perigo: true } : null,
  ] : []

  return (
    <aside className="cartao flex h-full min-h-0 flex-col" aria-label="Pedido actual">
      <div className="flex items-start justify-between gap-2 border-b border-slate-200 p-3">
        <div className="min-w-0">
          <p className="text-lg font-semibold">{cabecalho}</p>
          {pedido && (
            <p className="text-xs text-slate-500">
              {pedido.numero}
              {pedido.estado !== 'Aberto' && <> · <strong className={pedido.estado === 'Pago' ? 'text-green-700' : 'text-red-700'}>{pedido.estado}</strong></>}
              {pedido.estadoEntrega && pedido.estado === 'Aberto' && ` · ${pedido.estadoEntrega}`}
            </p>
          )}
          {/* Responsável da mesa junto do número da mesa (quem a abriu ou a quem foi passada). */}
          {mesa && pedido?.responsavel && (
            <p className="mt-0.5 flex items-center gap-1.5 text-sm text-slate-700" data-testid="responsavel-pedido" title="Responsável da mesa">
              <UserCheck className="h-4 w-4 shrink-0 text-slate-500" aria-hidden />
              <span className="sr-only">Responsável da mesa: </span><strong className="truncate">{pedido.responsavel}</strong>
            </p>
          )}
          {pedido?.clienteNome && (
            <p className="mt-0.5 flex items-center gap-1.5 text-sm text-slate-700" data-testid="cliente-pedido">
              <UserRound className="h-4 w-4 shrink-0 text-slate-500" aria-hidden /><span className="truncate">Cliente: <strong>{pedido.clienteNome}</strong></span>
            </p>
          )}
          {pedido?.garcomNome && <p className="mt-0.5 text-xs text-slate-500">Garçom: {pedido.garcomNome}</p>}
          {pedido?.observacoes && (
            <p className="mt-0.5 flex items-start gap-1.5 text-sm">
              <MessageSquare className="mt-0.5 h-4 w-4 shrink-0 text-slate-500" aria-hidden />
              <span className="italic text-amber-700" data-testid="comentario-pedido">{pedido.observacoes}</span>
            </p>
          )}
        </div>
        <div className="flex shrink-0 gap-1">
          <MenuPedido opcoes={opcoesMenu} />
          <button type="button" className="btn-icone" onClick={accoes.fechar} aria-label="Fechar pedido no ecrã" title="Fechar (o pedido continua aberto)">
            <X className="h-5 w-5" />
          </button>
        </div>
      </div>

      <ul className="min-h-0 flex-1 divide-y divide-slate-100 overflow-y-auto">
        {itens.length === 0 && <li className="p-6 text-center text-sm text-slate-500">Toque nos produtos para adicionar.</li>}
        {itens.map((i) => {
          const estado = ESTADO_ITEM[i.estadoCozinha]
          const editavel = aberto && !i.cancelado && i.estadoCozinha === 'NaoEnviado'
          return (
            <li key={i.id} className={`px-3 py-2 ${i.cancelado ? 'opacity-50' : ''}`}>
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <p className={`font-medium ${i.cancelado ? 'line-through' : ''}`}>{i.nomeProduto}</p>
                  <p className="text-xs text-slate-500">{formatarMoeda(i.precoUnitario)} / {i.unidade}</p>
                  {i.observacoes && <p className="text-sm italic text-amber-700" data-testid="comentario-item">— {i.observacoes}</p>}
                  {i.cancelado && <p className="text-xs text-red-600">Cancelado: {i.motivoCancelamento}</p>}
                </div>
                <div className="text-right">
                  <p className="font-semibold tabular-nums">{formatarMoeda(i.total)}</p>
                  {!i.cancelado && i.enviaCozinha && <Etiqueta cor={estado.cor}>{estado.texto}</Etiqueta>}
                  {!i.cancelado && !i.enviaCozinha && i.estadoCozinha === 'NaoEnviado' && <Etiqueta cor="cinza">Por enviar</Etiqueta>}
                </div>
              </div>
              {aberto && !i.cancelado && (
                <div className="mt-1 flex items-center gap-1">
                  {editavel ? (
                    <>
                      <button type="button" className="btn-icone h-9 w-9" disabled={bloqueado} aria-label={`Diminuir ${i.nomeProduto}`}
                        onClick={() => (i.quantidade > 1 ? accoes.alterarQuantidade(i, i.quantidade - 1) : accoes.remover(i))}>
                        {i.quantidade > 1 ? <Minus className="h-4 w-4" /> : <Trash2 className="h-4 w-4 text-red-600" />}
                      </button>
                      <span className="min-w-10 text-center font-semibold tabular-nums">{quantidade(i.quantidade)}</span>
                      <button type="button" className="btn-icone h-9 w-9" disabled={bloqueado} aria-label={`Aumentar ${i.nomeProduto}`}
                        onClick={() => accoes.alterarQuantidade(i, i.quantidade + 1)}>
                        <Plus className="h-4 w-4" />
                      </button>
                      <button type="button" className="ml-auto inline-flex min-h-9 items-center gap-1 rounded-lg px-2 text-xs font-medium text-primaria-700 hover:bg-primaria-50"
                        disabled={bloqueado} aria-label={`Comentário de ${i.nomeProduto}`} onClick={() => accoes.observacoes(i)}>
                        <MessageSquare className="h-4 w-4" aria-hidden /> Comentário
                      </button>
                    </>
                  ) : (
                    <>
                      <span className="text-sm text-slate-600">Qtd. {quantidade(i.quantidade)}</span>
                      <button type="button" className="ml-auto inline-flex min-h-9 items-center gap-1 rounded-lg px-2 text-xs font-medium text-primaria-700 hover:bg-primaria-50"
                        disabled={bloqueado} aria-label={`Comentário de ${i.nomeProduto}`} title="Depois do envio, sai um talão de alteração" onClick={() => accoes.observacoes(i)}>
                        <MessageSquare className="h-4 w-4" aria-hidden /> Comentário
                      </button>
                      {permissoes.cancelarItem && (
                        <button type="button" className="inline-flex min-h-9 items-center gap-1 rounded-lg px-2 text-xs font-medium text-red-700 hover:bg-red-50"
                          disabled={bloqueado} onClick={() => accoes.cancelarItem(i)}>
                          <Ban className="h-3.5 w-3.5" aria-hidden /> Cancelar item
                        </button>
                      )}
                    </>
                  )}
                </div>
              )}
            </li>
          )
        })}
      </ul>

      {pedido && (
        <div className="space-y-1 border-t border-slate-200 p-3 text-sm tabular-nums">
          <div className="flex justify-between"><span>Subtotal</span><span>{formatarMoeda(pedido.subtotal)}</span></div>
          {pedido.desconto > 0 && <div className="flex justify-between text-red-700"><span>Desconto</span><span>−{formatarMoeda(pedido.desconto)}</span></div>}
          {pedido.taxaEntrega > 0 && <div className="flex justify-between"><span>Taxa de entrega</span><span>{formatarMoeda(pedido.taxaEntrega)}</span></div>}
          <div className="flex justify-between text-xs text-slate-500"><span>Impostos incluídos</span><span>{formatarMoeda(pedido.impostos)}</span></div>
          <div className="flex justify-between pt-1 text-xl font-bold"><span>Total</span><span>{formatarMoeda(pedido.total)}</span></div>
          {pedido.totalPago > 0 && (
            <>
              <div className="flex justify-between text-green-700"><span>Pago</span><span>{formatarMoeda(pedido.totalPago)}</span></div>
              <div className="flex justify-between font-semibold"><span>Em dívida</span><span>{formatarMoeda(pedido.emDivida)}</span></div>
            </>
          )}
        </div>
      )}

      {pedido && aberto && (
        <div className="grid grid-cols-2 gap-2 border-t border-slate-200 p-3">
          {/* Balcão: os talões saem ao pagar. */}
          {pedido.tipo !== 'Balcao' && (
            <button type="button" className="btn-secundario" disabled={bloqueado || porEnviar === 0} onClick={accoes.enviarCozinha}>
              <ChefHat className="h-4 w-4" aria-hidden /> Enviar {porEnviar > 0 ? `(${porEnviar})` : ''}
            </button>
          )}
          <button type="button" className={`btn-secundario ${pedido.tipo === 'Balcao' ? 'col-span-2' : ''}`} disabled={bloqueado || pedido.total <= 0} onClick={accoes.pedirConta}
            title="Imprime a conta (consulta do total antes de pagar; não é recibo)">
            <ReceiptText className="h-4 w-4" aria-hidden /> Pedir conta
          </button>
          {permissoes.pagar && (
            <button type="button" className="btn-primario col-span-2 min-h-14 text-base" disabled={bloqueado || pedido.emDivida <= 0} onClick={accoes.pagar}>
              <Wallet className="h-5 w-5" aria-hidden /> Pagar {formatarMoeda(pedido.emDivida)}
            </button>
          )}
        </div>
      )}
      {pedido?.estado === 'Pago' && (
        <div className="grid gap-2 border-t border-slate-200 p-3">
          <button type="button" className="btn-secundario w-full" onClick={accoes.imprimirRecibo}>
            <Printer className="h-4 w-4" aria-hidden /> Imprimir {documentoVenda(pedido.reciboNumero)}
          </button>
          {permissoes.estornar && (
            <button type="button" className="btn-secundario w-full text-red-700" disabled={bloqueado} onClick={accoes.estornar}>
              <Undo2 className="h-4 w-4" aria-hidden /> Estornar pedido
            </button>
          )}
        </div>
      )}
      {(pedido?.estado === 'Cancelado' || pedido?.estado === 'Estornado') && pedido.motivoCancelamento && (
        <p className="border-t border-slate-200 p-3 text-sm text-red-700">
          {pedido.estado === 'Estornado' ? 'Estornado' : 'Cancelado'}: {pedido.motivoCancelamento}
        </p>
      )}
    </aside>
  )
}

interface OpcaoMenu {
  texto: string
  icone: ReactNode
  accao: () => void
  desactivada?: boolean
  perigo?: boolean
  dica?: string
}

/** Botão «⋯» com as opções do pedido; `null` separa grupos (sem separadores no início, no fim ou repetidos). */
function MenuPedido({ opcoes }: { opcoes: (OpcaoMenu | null)[] }) {
  const [aberto, setAberto] = useState(false)
  const raiz = useRef<HTMLDivElement>(null)
  const grupos: OpcaoMenu[][] = [[]]
  for (const o of opcoes) {
    if (o) grupos[grupos.length - 1].push(o)
    else grupos.push([])
  }
  const visiveis = grupos.filter((g) => g.length > 0)

  useEffect(() => {
    if (!aberto) return
    const fora = (e: PointerEvent) => { if (!raiz.current?.contains(e.target as Node)) setAberto(false) }
    const tecla = (e: KeyboardEvent) => { if (e.key === 'Escape') setAberto(false) }
    document.addEventListener('pointerdown', fora)
    document.addEventListener('keydown', tecla)
    return () => {
      document.removeEventListener('pointerdown', fora)
      document.removeEventListener('keydown', tecla)
    }
  }, [aberto])
  // Pedido fechado ou pago: o menu desaparece e não pode ficar aberto.
  useEffect(() => { if (visiveis.length === 0) setAberto(false) }, [visiveis.length])

  if (visiveis.length === 0) return null
  return (
    <div ref={raiz} className="relative">
      <button type="button" className={`btn-icone ${aberto ? 'bg-slate-100' : ''}`} aria-label="Mais opções do pedido" title="Mais opções do pedido"
        aria-haspopup="menu" aria-expanded={aberto} onClick={() => setAberto((x) => !x)}>
        <Ellipsis className="h-5 w-5" />
      </button>
      {aberto && (
        <div role="menu" aria-label="Opções do pedido"
          className="absolute right-0 top-full z-30 mt-1 w-64 rounded-xl border border-slate-200 bg-white p-1.5 shadow-lg">
          {visiveis.map((g, i) => (
            <div key={i} className={i > 0 ? 'mt-1 border-t border-slate-200 pt-1' : ''}>
              {g.map((o) => (
                <button key={o.texto} type="button" role="menuitem" disabled={o.desactivada} title={o.dica}
                  onClick={() => { setAberto(false); o.accao() }}
                  className={`flex min-h-11 w-full items-center gap-3 rounded-lg px-3 text-left text-sm font-medium hover:bg-slate-100 disabled:opacity-40 disabled:hover:bg-transparent tactil:min-h-12 tactil:text-base ${o.perigo ? 'text-red-700' : 'text-slate-800'}`}>
                  <span className={o.perigo ? 'text-red-600' : 'text-slate-500'} aria-hidden>{o.icone}</span>
                  {o.texto}
                </button>
              ))}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
