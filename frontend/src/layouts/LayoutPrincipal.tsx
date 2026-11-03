import { useEffect, useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import {
  BarChart3, Boxes, CalendarClock, ChefHat, ClipboardList, Cog, FileSearch, Home, KeyRound, LayoutGrid, LogOut, Menu,
  Package, Radio, Receipt, ShieldCheck, ShoppingCart, Truck, UserRound, Users, Wallet, type LucideIcon,
} from 'lucide-react'
import { DESCRICAO_LIGACAO, useLigacao, type EstadoLigacao } from '../contexts/LigacaoContext'
import { useTempoReal } from '../contexts/TempoRealContext'
import { useSessao } from '../contexts/SessaoContext'
import { REGIONAL_PADRAO } from '../utils/formatar'

interface ItemMenu {
  rotulo: string
  icone: LucideIcon
  /** Rota implementada; sem rota o módulo aparece como "brevemente" (sem acção). */
  rota?: string
  /** Basta uma destas permissões para ver o item. Sem lista = qualquer utilizador autenticado. */
  permissoes?: string[]
}

const GRUPOS: { titulo: string; itens: ItemMenu[] }[] = [
  {
    titulo: 'Operação',
    itens: [
      { rotulo: 'Início', icone: Home, rota: '/' },
      { rotulo: 'POS', icone: ShoppingCart, rota: '/pos', permissoes: ['pos.usar'] },
      { rotulo: 'Mesas', icone: LayoutGrid, rota: '/mesas', permissoes: ['mesas.ver', 'mesas.gerir'] },
      { rotulo: 'Cozinha', icone: ChefHat, rota: '/cozinha', permissoes: ['cozinha.ver'] },
      { rotulo: 'Caixa', icone: Wallet, rota: '/caixa', permissoes: ['caixa.operar', 'caixa.ver-todas'] },
      { rotulo: 'Reservas', icone: CalendarClock, permissoes: ['reservas.gerir'] },
      { rotulo: 'Delivery', icone: Truck, rota: '/delivery', permissoes: ['delivery.gerir'] },
    ],
  },
  {
    titulo: 'Gestão',
    itens: [
      { rotulo: 'Produtos', icone: Package, rota: '/produtos', permissoes: ['produtos.ver'] },
      { rotulo: 'Stock', icone: Boxes, rota: '/stock', permissoes: ['stock.ver', 'receitas.gerir'] },
      { rotulo: 'Compras', icone: ClipboardList, rota: '/compras', permissoes: ['compras.ver'] },
      { rotulo: 'Clientes', icone: UserRound, permissoes: ['clientes.ver'] },
      { rotulo: 'Despesas', icone: Receipt, permissoes: ['despesas.registar'] },
      { rotulo: 'Relatórios', icone: BarChart3, rota: '/relatorios', permissoes: ['relatorios.vendas'] },
    ],
  },
  {
    titulo: 'Administração',
    itens: [
      { rotulo: 'Utilizadores', icone: Users, rota: '/utilizadores', permissoes: ['utilizadores.gerir'] },
      { rotulo: 'Perfis e permissões', icone: ShieldCheck, rota: '/perfis', permissoes: ['perfis.gerir'] },
      { rotulo: 'Auditoria', icone: FileSearch, permissoes: ['auditoria.ver'] },
      { rotulo: 'Configurações', icone: Cog, permissoes: ['configuracoes.gerir'] },
    ],
  },
]

const COR_LIGACAO: Record<EstadoLigacao, string> = {
  'a-verificar': 'bg-slate-400',
  ligado: 'bg-green-500',
  'sem-servidor': 'bg-red-500',
  'sem-base-dados': 'bg-red-500',
  'migracoes-pendentes': 'bg-amber-500',
}

function useRelogio() {
  const [agora, setAgora] = useState(() => new Date())
  useEffect(() => {
    const id = window.setInterval(() => setAgora(new Date()), 15_000)
    return () => window.clearInterval(id)
  }, [])
  return agora
}

export function LayoutPrincipal() {
  const { utilizador, sair, tem } = useSessao()
  const { estado, podeGravar } = useLigacao()
  const { estado: tempoReal } = useTempoReal()
  const agora = useRelogio()
  const [menuAberto, setMenuAberto] = useState(false)

  const grupos = GRUPOS.map((g) => ({
    ...g,
    itens: g.itens.filter((i) => !i.permissoes || i.permissoes.some(tem)),
  })).filter((g) => g.itens.length > 0)

  // Alteração obrigatória de palavra-passe: sem menu (a API também bloqueia as restantes áreas).
  if (utilizador?.deveAlterarPalavraPasse) return <Outlet />

  const dataHora = new Intl.DateTimeFormat('pt-MZ', {
    weekday: 'short',
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: REGIONAL_PADRAO.fusoHorario,
  }).format(agora)

  return (
    <div className="flex h-full">
      {menuAberto && <div className="fixed inset-0 z-30 bg-slate-900/40 lg:hidden" onClick={() => setMenuAberto(false)} aria-hidden />}

      <aside
        className={`fixed inset-y-0 left-0 z-40 flex w-64 flex-col bg-primaria-900 text-primaria-100 transition-transform lg:static lg:translate-x-0 ${
          menuAberto ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className="flex h-16 items-center gap-3 border-b border-primaria-800 px-5">
          <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-primaria-600 font-bold text-white">LG</div>
          <div className="leading-tight">
            <p className="font-semibold text-white">LAMGest</p>
            <p className="text-xs text-primaria-200">Restaurant ERP</p>
          </div>
        </div>
        <nav className="flex-1 overflow-y-auto px-3 py-4" aria-label="Menu principal">
          {grupos.map((g) => (
            <div key={g.titulo} className="mb-5">
              <p className="mb-1 px-3 text-xs font-semibold uppercase tracking-wide text-primaria-300">{g.titulo}</p>
              <ul className="space-y-0.5">
                {g.itens.map((i) => (
                  <li key={i.rotulo}>
                    {i.rota ? (
                      <NavLink
                        to={i.rota}
                        end={i.rota === '/'}
                        onClick={() => setMenuAberto(false)}
                        className={({ isActive }) =>
                          `flex min-h-11 items-center gap-3 rounded-lg px-3 text-sm font-medium ${
                            isActive ? 'bg-primaria-700 text-white' : 'hover:bg-primaria-800 hover:text-white'
                          }`
                        }
                      >
                        <i.icone className="h-5 w-5" aria-hidden />
                        {i.rotulo}
                      </NavLink>
                    ) : (
                      <span className="flex min-h-11 cursor-default items-center gap-3 rounded-lg px-3 text-sm text-primaria-300/70" title="Módulo em desenvolvimento">
                        <i.icone className="h-5 w-5" aria-hidden />
                        {i.rotulo}
                        <span className="ml-auto rounded bg-primaria-800 px-1.5 py-0.5 text-[10px] uppercase">Brevemente</span>
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4">
          <button type="button" className="btn-icone lg:hidden" onClick={() => setMenuAberto(true)} aria-label="Abrir menu">
            <Menu className="h-6 w-6" />
          </button>

          <div className="flex items-center gap-2 text-sm" title={DESCRICAO_LIGACAO[estado]}>
            <span className={`h-3 w-3 rounded-full ${COR_LIGACAO[estado]} ${estado === 'ligado' ? '' : 'animate-pulse'}`} aria-hidden />
            <span className="hidden text-slate-600 sm:inline">{DESCRICAO_LIGACAO[estado]}</span>
          </div>
          {estado === 'ligado' && (
            <span className="hidden items-center gap-1.5 text-xs text-slate-500 md:flex" title="Actualizações em tempo real (cozinha, mesas, pedidos)">
              <Radio className={`h-4 w-4 ${tempoReal === 'ligado' ? 'text-green-600' : 'animate-pulse text-amber-500'}`} aria-hidden />
              {tempoReal === 'ligado' ? 'Tempo real' : 'Tempo real a religar…'}
            </span>
          )}

          <div className="ml-auto flex items-center gap-4">
            <span className="hidden text-sm capitalize text-slate-600 md:inline">{dataHora}</span>
            <div className="text-right leading-tight">
              <p className="text-sm font-medium">{utilizador?.nomeCompleto}</p>
              <p className="text-xs text-slate-500">{utilizador?.perfis.join(', ')}</p>
            </div>
            <NavLink to="/alterar-palavra-passe" className="btn-icone" title="Alterar palavra-passe" aria-label="Alterar palavra-passe">
              <KeyRound className="h-5 w-5" />
            </NavLink>
            <button type="button" className="btn-icone" onClick={() => void sair()} title="Terminar sessão" aria-label="Terminar sessão">
              <LogOut className="h-5 w-5" />
            </button>
          </div>
        </header>

        {!podeGravar && estado !== 'a-verificar' && (
          <div role="alert" className="bg-red-600 px-4 py-2 text-center text-sm font-medium text-white">
            {DESCRICAO_LIGACAO[estado]}. As operações que gravam dados estão bloqueadas até a ligação ser restabelecida.
          </div>
        )}

        <main className="flex-1 overflow-y-auto p-4 lg:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
