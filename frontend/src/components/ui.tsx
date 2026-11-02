import { useEffect, useRef, type ReactNode } from 'react'
import { AlertTriangle, CheckCircle2, ChevronLeft, ChevronRight, Inbox, Info, Loader2, X, XCircle } from 'lucide-react'

export function Carregando({ texto = 'A carregar…' }: { texto?: string }) {
  return (
    <div role="status" className="flex items-center justify-center gap-2 py-10 text-slate-500">
      <Loader2 className="h-5 w-5 animate-spin" aria-hidden />
      <span>{texto}</span>
    </div>
  )
}

export function Vazio({ texto, children }: { texto: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-12 text-slate-500">
      <Inbox className="h-8 w-8" aria-hidden />
      <p>{texto}</p>
      {children}
    </div>
  )
}

type TipoAlerta = 'erro' | 'aviso' | 'info' | 'sucesso'

const ESTILO_ALERTA: Record<TipoAlerta, { classe: string; Icone: typeof Info }> = {
  erro: { classe: 'border-red-200 bg-red-50 text-red-800', Icone: XCircle },
  aviso: { classe: 'border-amber-200 bg-amber-50 text-amber-900', Icone: AlertTriangle },
  info: { classe: 'border-primaria-200 bg-primaria-50 text-primaria-900', Icone: Info },
  sucesso: { classe: 'border-green-200 bg-green-50 text-green-800', Icone: CheckCircle2 },
}

export function Alerta({ tipo, titulo, children }: { tipo: TipoAlerta; titulo?: string; children?: ReactNode }) {
  const { classe, Icone } = ESTILO_ALERTA[tipo]
  return (
    <div role={tipo === 'erro' ? 'alert' : 'status'} className={`flex gap-3 rounded-lg border p-3 text-sm ${classe}`}>
      <Icone className="mt-0.5 h-5 w-5 shrink-0" aria-hidden />
      <div>
        {titulo && <p className="font-semibold">{titulo}</p>}
        {children}
      </div>
    </div>
  )
}

export function Modal({
  titulo,
  aoFechar,
  children,
  rodape,
  largura = 'max-w-lg',
}: {
  titulo: string
  aoFechar: () => void
  children: ReactNode
  rodape?: ReactNode
  largura?: string
}) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const tecla = (e: KeyboardEvent) => e.key === 'Escape' && aoFechar()
    window.addEventListener('keydown', tecla)
    ref.current?.querySelector<HTMLElement>('input, select, textarea, button')?.focus()
    return () => window.removeEventListener('keydown', tecla)
  }, [aoFechar])

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4" role="dialog" aria-modal="true" aria-label={titulo}>
      <div ref={ref} className={`cartao flex max-h-[90vh] w-full flex-col ${largura}`}>
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-3">
          <h2 className="text-lg font-semibold">{titulo}</h2>
          <button type="button" className="btn-icone" onClick={aoFechar} aria-label="Fechar">
            <X className="h-5 w-5" />
          </button>
        </div>
        <div className="overflow-y-auto px-5 py-4">{children}</div>
        {rodape && <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-3">{rodape}</div>}
      </div>
    </div>
  )
}

/** Confirmação obrigatória antes de acções críticas. */
export function Confirmacao({
  titulo,
  mensagem,
  textoConfirmar,
  perigo = false,
  aProcessar = false,
  aoConfirmar,
  aoCancelar,
}: {
  titulo: string
  mensagem: ReactNode
  textoConfirmar: string
  perigo?: boolean
  aProcessar?: boolean
  aoConfirmar: () => void
  aoCancelar: () => void
}) {
  return (
    <Modal
      titulo={titulo}
      aoFechar={aoCancelar}
      rodape={
        <>
          <button type="button" className="btn-secundario" onClick={aoCancelar} disabled={aProcessar}>
            Cancelar
          </button>
          <button type="button" className={perigo ? 'btn-perigo' : 'btn-primario'} onClick={aoConfirmar} disabled={aProcessar}>
            {aProcessar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
            {textoConfirmar}
          </button>
        </>
      }
    >
      <div className="text-sm text-slate-700">{mensagem}</div>
    </Modal>
  )
}

export function Paginacao({
  pagina,
  tamanho,
  total,
  aoMudar,
}: {
  pagina: number
  tamanho: number
  total: number
  aoMudar: (pagina: number) => void
}) {
  const paginas = Math.max(1, Math.ceil(total / tamanho))
  const inicio = total === 0 ? 0 : (pagina - 1) * tamanho + 1
  const fim = Math.min(total, pagina * tamanho)
  return (
    <div className="flex items-center justify-between border-t border-slate-200 px-4 py-2 text-sm text-slate-600">
      <span>
        {inicio}–{fim} de {total}
      </span>
      <div className="flex items-center gap-1">
        <button type="button" className="btn-icone" disabled={pagina <= 1} onClick={() => aoMudar(pagina - 1)} aria-label="Página anterior">
          <ChevronLeft className="h-5 w-5" />
        </button>
        <span>
          Página {pagina} de {paginas}
        </span>
        <button type="button" className="btn-icone" disabled={pagina >= paginas} onClick={() => aoMudar(pagina + 1)} aria-label="Página seguinte">
          <ChevronRight className="h-5 w-5" />
        </button>
      </div>
    </div>
  )
}

export function Etiqueta({ cor, children }: { cor: 'verde' | 'cinza' | 'vermelho' | 'amarelo' | 'azul'; children: ReactNode }) {
  const classes = {
    verde: 'bg-green-100 text-green-800',
    cinza: 'bg-slate-200 text-slate-700',
    vermelho: 'bg-red-100 text-red-800',
    amarelo: 'bg-amber-100 text-amber-900',
    azul: 'bg-primaria-100 text-primaria-800',
  }[cor]
  return <span className={`inline-flex items-center whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium ${classes}`}>{children}</span>
}

/** Mensagem de erro de um campo de formulário. */
export function ErroCampo({ mensagem }: { mensagem?: string }) {
  if (!mensagem) return null
  return <p className="mt-1 text-sm text-red-600">{mensagem}</p>
}
