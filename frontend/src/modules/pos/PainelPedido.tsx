import { ArrowRightLeft, ChefHat, Ban, MessageSquare, Minus, Percent, Plus, Printer, ReceiptText, Trash2, Undo2, UserRound, Wallet, X } from 'lucide-react'
import { Etiqueta } from '../../components/ui'
import type { EstadoItemCozinha, Pedido, PedidoItem } from '../../types/api'
import { documentoVenda, formatarMoeda } from '../../utils/formatar'

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
  cancelarItem: (item: PedidoItem) => void
  enviarCozinha: () => void
  desconto: () => void
  transferir: () => void
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
  permissoes: { desconto: boolean; cancelarItem: boolean; cancelarPedido: boolean; transferir: boolean; pagar: boolean; estornar: boolean }
  accoes: AccoesPedido
}) {
  // Itens retirados antes do envio não interessam ao operador; os cancelados depois do envio mostram-se riscados.
  const itens = pedido?.itens.filter((i) => !(i.cancelado && i.estadoCozinha === 'NaoEnviado')) ?? []
  const porEnviar = itens.filter((i) => !i.cancelado && i.estadoCozinha === 'NaoEnviado').length
  const enviados = itens.filter((i) => !i.cancelado && i.estadoCozinha !== 'NaoEnviado').length
  const aberto = pedido?.estado === 'Aberto'
  const bloqueado = aGravar || !podeGravar

  return (
    <aside className="cartao flex h-full min-h-0 flex-col" aria-label="Pedido actual">
      <div className="flex items-start justify-between gap-2 border-b border-slate-200 p-3">
        <div>
          <p className="text-lg font-semibold">{cabecalho}</p>
          {pedido && (
            <p className="text-xs text-slate-500">
              {pedido.numero}
              {pedido.estado !== 'Aberto' && <> · <strong className={pedido.estado === 'Pago' ? 'text-green-700' : 'text-red-700'}>{pedido.estado}</strong></>}
              {pedido.estadoEntrega && pedido.estado === 'Aberto' && ` · ${pedido.estadoEntrega}`}
            </p>
          )}
        </div>
        <button type="button" className="btn-icone" onClick={accoes.fechar} aria-label="Fechar pedido no ecrã" title="Fechar (o pedido continua aberto)">
          <X className="h-5 w-5" />
        </button>
      </div>
      {pedido && (pedido.clienteNome || aberto) && (
        <button type="button" disabled={!aberto || bloqueado} onClick={accoes.cliente}
          className="flex min-h-10 items-center gap-2 border-b border-slate-200 px-3 text-left text-sm text-slate-700 hover:bg-slate-50 disabled:hover:bg-transparent">
          <UserRound className="h-4 w-4 text-slate-500" aria-hidden />
          {pedido.clienteNome ? <span>Cliente: <strong>{pedido.clienteNome}</strong></span> : <span className="text-primaria-700">Associar cliente (fidelização)</span>}
          {pedido.garcomNome && <span className="ml-auto text-xs text-slate-500">Garçom: {pedido.garcomNome}</span>}
        </button>
      )}

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
                  <p className="text-xs text-slate-500">{formatarMoeda(i.precoUnitario)} / un.</p>
                  {i.observacoes && <p className="text-xs italic text-amber-700">{i.observacoes}</p>}
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
                      <span className="w-10 text-center font-semibold tabular-nums">{i.quantidade}</span>
                      <button type="button" className="btn-icone h-9 w-9" disabled={bloqueado} aria-label={`Aumentar ${i.nomeProduto}`}
                        onClick={() => accoes.alterarQuantidade(i, i.quantidade + 1)}>
                        <Plus className="h-4 w-4" />
                      </button>
                      <button type="button" className="btn-icone ml-auto h-9 w-9" disabled={bloqueado} aria-label={`Observações de ${i.nomeProduto}`} title="Observações"
                        onClick={() => accoes.observacoes(i)}>
                        <MessageSquare className="h-4 w-4" />
                      </button>
                    </>
                  ) : (
                    <>
                      <span className="text-sm text-slate-600">Qtd. {i.quantidade}</span>
                      {permissoes.cancelarItem && (
                        <button type="button" className="ml-auto inline-flex min-h-9 items-center gap-1 rounded-lg px-2 text-xs font-medium text-red-700 hover:bg-red-50"
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
          <button type="button" className={`btn-secundario ${enviados > 0 ? '' : 'col-span-2'}`} disabled={bloqueado || porEnviar === 0} onClick={accoes.enviarCozinha}>
            <ChefHat className="h-4 w-4" aria-hidden /> Enviar {porEnviar > 0 ? `(${porEnviar})` : ''}
          </button>
          {enviados > 0 && (
            <button type="button" className="btn-secundario" disabled={bloqueado} onClick={accoes.reimprimirTaloes} title="Volta a imprimir nos centros de produção o que já foi enviado">
              <Printer className="h-4 w-4" aria-hidden /> Reimprimir talões
            </button>
          )}
          {permissoes.desconto && (
            <button type="button" className="btn-secundario" disabled={bloqueado || pedido.subtotal === 0} onClick={accoes.desconto}>
              <Percent className="h-4 w-4" aria-hidden /> Desconto
            </button>
          )}
          {permissoes.transferir && pedido.tipo === 'Mesa' && (
            <button type="button" className="btn-secundario" disabled={bloqueado} onClick={accoes.transferir}>
              <ArrowRightLeft className="h-4 w-4" aria-hidden /> Transferir
            </button>
          )}
          {permissoes.cancelarPedido && (
            <button type="button" className="btn-secundario text-red-700" disabled={bloqueado} onClick={accoes.cancelarPedido}>
              <Ban className="h-4 w-4" aria-hidden /> Cancelar
            </button>
          )}
          <button type="button" className="btn-secundario col-span-2" disabled={bloqueado || pedido.total <= 0} onClick={accoes.pedirConta}
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
