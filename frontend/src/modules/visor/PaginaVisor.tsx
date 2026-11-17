import { useEffect, useRef, useState } from 'react'
import type { EcraVisor } from '../../types/api'
import { formatarMoeda, REGIONAL_PADRAO } from '../../utils/formatar'

/** Título da janela: é por ele que o agente do posto encontra a janela do visor no segundo monitor (não alterar). */
export const TITULO_VISOR = 'LAMGest — Visor do cliente'

const esperar = (ms: number) => new Promise((r) => window.setTimeout(r, ms))

/**
 * Visor do cliente (/visor?terminal=ID&chave=...): aberto pelo agente do posto em ecrã inteiro no segundo monitor, ou num tablet virado
 * para o cliente. Sem sessão: só a chave do endereço. Pedido longo ao servidor, que responde logo que o POS do posto muda o pedido no ecrã.
 */
export function PaginaVisor() {
  const parametros = new URLSearchParams(window.location.search)
  const terminal = parametros.get('terminal') ?? ''
  const chave = parametros.get('chave') ?? ''
  const [ecra, setEcra] = useState<EcraVisor | null>(null)
  const [estado, setEstado] = useState<'ligado' | 'sem-ligacao' | 'desligado'>('ligado')

  useEffect(() => {
    document.title = TITULO_VISOR
    let activo = true
    let versao = 0
    void (async () => {
      while (activo) {
        try {
          const r = await fetch(`/api/visor/ecra?terminal=${encodeURIComponent(terminal)}&chave=${encodeURIComponent(chave)}&versao=${versao}&espera=20`, { cache: 'no-store' })
          if (!activo) return
          if (r.status === 401 || r.status === 400) {
            setEstado('desligado')
            await esperar(15_000)
            continue
          }
          if (!r.ok) throw new Error(String(r.status))
          if (r.status !== 204) {
            const dados = (await r.json()) as EcraVisor
            versao = dados.versao
            setEcra(dados)
          }
          setEstado('ligado')
        } catch {
          if (!activo) return
          setEstado('sem-ligacao')
          await esperar(5_000)
        }
      }
    })()
    return () => { activo = false }
  }, [terminal, chave])

  if (estado === 'desligado') {
    return <Fundo><p className="text-2xl text-slate-300">Visor do cliente desligado.</p><p className="mt-2 text-slate-500">Configure-o em Configurações → Postos.</p></Fundo>
  }
  if (!ecra) return <Fundo><p className="text-2xl text-slate-400">{estado === 'sem-ligacao' ? 'Sem ligação ao servidor…' : 'A ligar…'}</p></Fundo>

  const moeda = { ...REGIONAL_PADRAO, simboloMoeda: ecra.moeda || REGIONAL_PADRAO.simboloMoeda }
  const valor = (v: number) => formatarMoeda(v, moeda)
  const c = ecra.conteudo
  const logotipo = ecra.temLogotipo ? `/api/visor/logotipo?terminal=${encodeURIComponent(terminal)}&chave=${encodeURIComponent(chave)}` : null

  return (
    <div className="flex h-screen cursor-none select-none flex-col overflow-hidden bg-slate-900 text-white" aria-label="Visor do cliente" data-fase={c.fase}>
      <header className="flex items-center gap-4 border-b border-white/10 px-8 py-4">
        {logotipo && <img src={logotipo} alt="" className="h-14 w-auto rounded bg-white object-contain p-1" />}
        <p className="text-2xl font-semibold">{ecra.estabelecimento}</p>
        {estado === 'sem-ligacao' && <p className="ml-auto text-sm text-amber-300">Sem ligação ao servidor</p>}
      </header>

      {(c.fase === 'Repouso' || c.fase === 'Teste') && (
        <main className="flex flex-1 flex-col items-center justify-center gap-6 p-8 text-center">
          {logotipo && <img src={logotipo} alt="" className="max-h-48 max-w-md rounded-xl bg-white object-contain p-3" />}
          <p className="text-5xl font-bold leading-tight">{c.mensagem.replace(' | ', ' — ')}</p>
        </main>
      )}

      {(c.fase === 'Venda' || c.fase === 'Pagamento') && <Venda conteudo={c} valor={valor} />}

      {c.fase === 'Peso' && c.pesagem && (
        <main className="flex flex-1 flex-col items-center justify-center gap-6 p-8 text-center">
          <p className="text-4xl font-semibold">{c.pesagem.produto}</p>
          <p className="text-8xl font-bold tabular-nums" aria-label="Peso">
            {c.pesagem.peso.toLocaleString('pt-PT', { minimumFractionDigits: c.pesagem.unidade === 'kg' ? 3 : 0, maximumFractionDigits: 3 })} {c.pesagem.unidade}
          </p>
          <p className="text-2xl text-slate-300">{valor(c.pesagem.precoUnitario)} / {c.pesagem.unidade}</p>
          <p className="text-6xl font-bold tabular-nums text-emerald-400" aria-label="Valor">{valor(c.pesagem.valor)}</p>
        </main>
      )}

      {c.fase === 'Pago' && (
        <main className="flex flex-1 flex-col items-center justify-center gap-6 p-8 text-center">
          <p className="text-3xl text-slate-300">Total pago <span className="font-semibold text-white">{valor(c.total)}</span></p>
          {(c.troco ?? 0) > 0 && (
            <div>
              <p className="text-3xl text-slate-300">Troco</p>
              <p className="text-8xl font-bold text-emerald-400" aria-label="Troco">{valor(c.troco ?? 0)}</p>
            </div>
          )}
          <p className="text-5xl font-bold">{c.mensagem}</p>
        </main>
      )}
    </div>
  )
}

function Venda({ conteudo: c, valor }: { conteudo: EcraVisor['conteudo']; valor: (v: number) => string }) {
  const fim = useRef<HTMLLIElement>(null)
  useEffect(() => {
    fim.current?.scrollIntoView({ block: 'end' })
  }, [c.linhas.length])
  const quantidade = (q: number) => new Intl.NumberFormat('pt-MZ', { maximumFractionDigits: 3 }).format(q)

  return (
    <main className="grid min-h-0 flex-1 grid-cols-1 md:grid-cols-[1fr_minmax(18rem,38%)]">
      <ul className="min-h-0 overflow-hidden px-8 py-4 text-2xl" aria-label="Artigos">
        {c.linhas.map((l, i) => (
          <li key={i} ref={i === c.linhas.length - 1 ? fim : undefined}
            className={`flex items-baseline justify-between gap-4 border-b border-white/5 py-3 ${i === c.linhas.length - 1 ? 'font-semibold text-emerald-300' : ''}`}>
            <span className="min-w-0 truncate"><span className="text-slate-400">{quantidade(l.quantidade)} ×</span> {l.descricao}</span>
            <span className="shrink-0 tabular-nums">{valor(l.total)}</span>
          </li>
        ))}
      </ul>
      <aside className="flex flex-col justify-center gap-6 bg-slate-800 p-8">
        <div>
          <p className="text-2xl text-slate-300">{c.fase === 'Pagamento' ? 'A pagar' : 'Total'}</p>
          <p className="text-6xl font-bold tabular-nums" aria-label="Total">{valor(c.total)}</p>
        </div>
        {c.fase === 'Pagamento' && (
          <>
            <div><p className="text-xl text-slate-300">Recebido</p><p className="text-4xl font-semibold tabular-nums">{valor(c.recebido ?? 0)}</p></div>
            <div><p className="text-xl text-slate-300">Troco</p><p className="text-5xl font-bold tabular-nums text-emerald-400" aria-label="Troco">{valor(c.troco ?? 0)}</p></div>
          </>
        )}
      </aside>
    </main>
  )
}

function Fundo({ children }: { children: React.ReactNode }) {
  return <div className="flex h-screen cursor-none flex-col items-center justify-center bg-slate-900 p-8 text-center" aria-label="Visor do cliente">{children}</div>
}
