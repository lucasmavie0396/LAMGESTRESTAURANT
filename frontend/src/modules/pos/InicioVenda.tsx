import { Loader2, ShoppingBag, Store, Truck } from 'lucide-react'
import { Alerta } from '../../components/ui'
import type { Mesa, OpcoesPos } from '../../types/api'
import { GrelhaMesas } from './Dialogos'

/** Início da venda: Balcão, a mesa (directamente no mapa) ou um pedido Take Away / Delivery, conforme as configurações. */
export function InicioVenda({ mesas, erroMesas, podeGravar, opcoes, aoBalcao, aoEscolherMesa, aoEscolherEntrega }: {
  mesas: Mesa[] | null
  erroMesas: string | null
  podeGravar: boolean
  opcoes: OpcoesPos
  aoBalcao: () => void
  aoEscolherMesa: (mesa: Mesa) => void
  aoEscolherEntrega: (tipo: 'TakeAway' | 'Delivery') => void
}) {
  return (
    <div className="min-h-0 flex-1 space-y-4 overflow-y-auto rounded-xl bg-superficie p-4 ring-1 ring-slate-200">
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
