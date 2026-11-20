import { useEffect, useState } from 'react'
import { usePresencaPosto } from '../hooks/usePresencaPosto'
import { NavLink, Outlet } from 'react-router-dom'
import { OpcoesPda } from '../components/OpcoesPda'
import { emPda } from '../utils/pda'
import {
  BarChart3, Boxes, CalendarClock, ChefHat, ClipboardList, Cog, CreditCard, FileSearch, FileText, Home, IdCard, Keyboard, KeyRound, LayoutGrid, LogOut, Menu, PanelLeftClose, PanelLeftOpen,
  Moon, Package, Radio, Receipt, ShieldCheck, ShoppingCart, Sun, Truck, UserRound, Users, UsersRound, Wallet, type LucideIcon,
} from 'lucide-react'
import { useTema } from '../utils/tema'
import { SeletorIdioma } from '../components/SeletorIdioma'
import { localeDatas } from '../i18n/idioma'
import { DESCRICAO_LIGACAO, useLigacao, type EstadoLigacao } from '../contexts/LigacaoContext'
import { useTempoReal } from '../contexts/TempoRealContext'
import { useSessao } from '../contexts/SessaoContext'
import { REGIONAL_PADRAO } from '../utils/formatar'
import { lerLoginTactil, lerPostoLocal } from '../utils/terminalLocal'
import { VisualizadorPdf } from '../components/VisualizadorPdf'
import { SeletorLoja } from '../components/SeletorLoja'
import { P } from '../utils/permissoes'
import { guardarModoTeclado, lerModoTeclado, NOMES_MODO_TECLADO, type ModoTeclado } from '../utils/teclado'
import { api } from '../api/cliente'
import { useDados } from '../hooks/useDados'
import type { EstadoProducao } from '../types/api'

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
      { rotulo: 'Caixa', icone: Wallet, rota: '/caixa', permissoes: ['caixa.operar', 'caixa.ver-todas', 'caixa.dia'] },
      { rotulo: 'Cartões Evento', icone: CreditCard, rota: '/cartoes', permissoes: ['cartoes.ver'] },
      { rotulo: 'Reservas', icone: CalendarClock, rota: '/reservas', permissoes: ['reservas.gerir'] },
      { rotulo: 'Delivery', icone: Truck, rota: '/delivery', permissoes: ['delivery.gerir'] },
    ],
  },
  {
    titulo: 'Gestão',
    itens: [
      { rotulo: 'Produtos', icone: Package, rota: '/produtos', permissoes: ['produtos.ver'] },
      { rotulo: 'Stock', icone: Boxes, rota: '/stock', permissoes: ['stock.ver', 'receitas.gerir'] },
      { rotulo: 'Compras', icone: ClipboardList, rota: '/compras', permissoes: ['compras.ver'] },
      { rotulo: 'Clientes', icone: UserRound, rota: '/clientes', permissoes: ['clientes.ver'] },
      { rotulo: 'Despesas', icone: Receipt, rota: '/despesas', permissoes: ['despesas.registar'] },
      { rotulo: 'Funcionários', icone: IdCard, rota: '/funcionarios', permissoes: ['funcionarios.ver'] },
      { rotulo: 'Facturação', icone: FileText, rota: '/facturacao', permissoes: ['facturacao.ver'] },
      { rotulo: 'Relatórios', icone: BarChart3, rota: '/relatorios', permissoes: ['relatorios.vendas', 'relatorios.financeiros', 'relatorios.stock', 'facturacao.ver'] },
    ],
  },
  {
    titulo: 'Administração',
    itens: [
      { rotulo: 'Utilizadores', icone: Users, rota: '/utilizadores', permissoes: ['utilizadores.gerir'] },
      { rotulo: 'Perfis e permissões', icone: ShieldCheck, rota: '/perfis', permissoes: ['perfis.gerir'] },
      { rotulo: 'Auditoria', icone: FileSearch, rota: '/auditoria', permissoes: ['auditoria.ver'] },
      { rotulo: 'Configurações', icone: Cog, rota: '/configuracoes', permissoes: ['configuracoes.gerir'] },
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
  const { estado, licenca } = useLigacao()
  const { estado: tempoReal } = useTempoReal()
  const agora = useRelogio()
  const [menuAberto, setMenuAberto] = useState(false)
  const [recolhido, setRecolhido] = useState(lerMenuRecolhido)
  const alternarMenu = () => setRecolhido((r) => { guardarMenuRecolhido(!r); return !r })
  const [posto] = useState(lerPostoLocal)
  const [tactil] = useState(lerLoginTactil)
  usePresencaPosto(!!utilizador)
  // App Android: os mesmos menus do computador, conforme as permissões de quem entra; mais as opções do aparelho.
  const noPda = emPda()
  // Modo de testes (antes de iniciar a produção): faixa de aviso para todos.
  const producao = useDados(() => (utilizador ? api.get<EstadoProducao>('/api/sistema/producao') : Promise.resolve(null)), [utilizador?.id])

  const grupos = GRUPOS.map((g) => ({
    ...g,
    itens: g.itens.filter((i) => !i.permissoes || i.permissoes.some(tem)),
  })).filter((g) => g.itens.length > 0)

  // Alteração obrigatória de palavra-passe: sem menu (a API também bloqueia as restantes áreas).
  if (utilizador?.deveAlterarPalavraPasse) return <Outlet />

  const dataHora = new Intl.DateTimeFormat(localeDatas(), {
    weekday: 'short',
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: REGIONAL_PADRAO.fusoHorario,
  }).format(agora)

  return (
    <div className="flex h-full">
      {menuAberto && <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setMenuAberto(false)} aria-hidden />}

      <aside
        className={`tema-claro fixed inset-y-0 left-0 z-40 flex w-64 flex-col ${recolhido ? 'lg:w-16' : ''} bg-primaria-900 text-primaria-100 transition-transform lg:static lg:translate-x-0 ${
          menuAberto ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className={`flex h-14 shrink-0 items-center gap-3 border-b border-primaria-800 px-5 ${recolhido ? 'lg:justify-center lg:px-0' : ''}`}>
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primaria-600 font-bold text-white">LG</div>
          <div className={`leading-tight ${recolhido ? 'lg:hidden' : ''}`}>
            <p className="font-semibold text-white">LAMGest</p>
            <p className="text-xs text-primaria-200">Restaurant ERP</p>
          </div>
        </div>
        <nav className={`flex-1 overflow-y-auto px-3 py-4 ${recolhido ? 'lg:px-2' : ''}`} aria-label="Menu principal">
          {grupos.map((g) => (
            <div key={g.titulo} className={`mb-5 ${recolhido ? 'lg:mb-2 lg:border-b lg:border-primaria-800 lg:pb-2' : ''}`}>
              <p className={`mb-1 px-3 text-xs font-semibold uppercase tracking-wide text-primaria-300 ${recolhido ? 'lg:sr-only' : ''}`}>{g.titulo}</p>
              <ul className="space-y-0.5">
                {g.itens.map((i) => (
                  <li key={i.rotulo}>
                    {i.rota ? (
                      <NavLink
                        to={i.rota}
                        end={i.rota === '/'}
                        onClick={() => setMenuAberto(false)}
                        title={recolhido ? i.rotulo : undefined}
                        className={({ isActive }) =>
                          `flex min-h-11 items-center gap-3 rounded-lg px-3 text-sm font-medium ${recolhido ? 'lg:justify-center lg:px-0' : ''} ${
                            isActive ? 'bg-primaria-700 text-white' : 'hover:bg-primaria-800 hover:text-white'
                          }`
                        }
                      >
                        <i.icone className="h-5 w-5 shrink-0" aria-hidden />
                        <span className={recolhido ? 'lg:sr-only' : ''}>{i.rotulo}</span>
                      </NavLink>
                    ) : (
                      <span className={`flex min-h-11 cursor-default items-center gap-3 rounded-lg px-3 text-sm text-primaria-300/70 ${recolhido ? 'lg:justify-center lg:px-0' : ''}`}
                        title={`${i.rotulo}: módulo em desenvolvimento`}>
                        <i.icone className="h-5 w-5 shrink-0" aria-hidden />
                        <span className={recolhido ? 'lg:sr-only' : ''}>{i.rotulo}</span>
                        <span className={`ml-auto rounded ${recolhido ? 'lg:hidden' : ''} bg-primaria-800 px-1.5 py-0.5 text-[10px] uppercase`}>Brevemente</span>
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>
        {noPda && <OpcoesPda />}
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-superficie px-4">
          <button type="button" className="btn-icone lg:hidden" onClick={() => setMenuAberto(true)} aria-label="Abrir menu">
            <Menu className="h-6 w-6" />
          </button>
          <button type="button" className="btn-icone hidden lg:inline-flex" onClick={alternarMenu} aria-label={recolhido ? 'Mostrar o menu' : 'Recolher o menu'}
            title={recolhido ? 'Mostrar o menu completo' : 'Recolher o menu (só ícones), para dar mais espaço'}>
            {recolhido ? <PanelLeftOpen className="h-6 w-6" /> : <PanelLeftClose className="h-6 w-6" />}
          </button>

          <div className="flex items-center gap-2 text-sm" title={DESCRICAO_LIGACAO[estado]}>
            <span className={`h-3 w-3 rounded-full ${COR_LIGACAO[estado]} ${estado === 'ligado' ? '' : 'animate-pulse'}`} aria-hidden />
            <span className="hidden whitespace-nowrap text-slate-600 lg:inline">{DESCRICAO_LIGACAO[estado]}</span>
          </div>
          {posto && (
            <span className={`hidden rounded-full px-2.5 py-1 text-xs font-medium sm:inline ${posto.nome ? 'bg-primaria-50 text-primaria-800' : 'bg-amber-100 text-amber-900'}`}
              title={posto.nome ? 'Posto definido no instalador deste computador' : 'Registe este número em Caixa → Terminais'}>
              Posto {posto.numero}{posto.nome ? ` · ${posto.nome}` : ' não registado'}
            </span>
          )}
          {estado === 'ligado' && (
            <span className="hidden items-center gap-1.5 whitespace-nowrap text-xs text-slate-500 xl:flex" title="Actualizações em tempo real (cozinha, mesas, pedidos)">
              <Radio className={`h-4 w-4 ${tempoReal === 'ligado' ? 'text-green-600' : 'animate-pulse text-amber-500'}`} aria-hidden />
              {tempoReal === 'ligado' ? 'Tempo real' : 'Tempo real a religar…'}
            </span>
          )}

          <div className="ml-auto flex min-w-0 items-center gap-2 sm:gap-4">
            <span className="hidden whitespace-nowrap text-sm capitalize text-slate-600 xl:inline">{dataHora}</span>
            <div className="min-w-0 text-right leading-tight">
              <p className="max-w-36 truncate text-sm font-medium sm:max-w-none" translate="no">{utilizador?.nomeCompleto}</p>
              <p className="hidden max-w-48 truncate text-xs text-slate-500 sm:block">{utilizador?.perfis.join(', ')}</p>
            </div>
            <SeletorLoja variante="topo" podeMudar={tem(P.lojasMudar)} />
            {!noPda && <ModoTecladoEcra />}
            <SeletorIdioma />
            <BotaoTema />
            <NavLink to="/alterar-palavra-passe" className="btn-icone" title="Alterar palavra-passe" aria-label="Alterar palavra-passe">
              <KeyRound className="h-5 w-5" />
            </NavLink>
            {tactil ? (
              // Posto com ecrã táctil: sair leva de volta aos botões dos utilizadores (o próximo entra com o seu código).
              <button type="button" className="btn-secundario min-h-11" onClick={() => void sair()} title="Terminar sessão e escolher outro utilizador">
                <UsersRound className="h-5 w-5" aria-hidden /> <span className="hidden sm:inline">Trocar utilizador</span>
              </button>
            ) : (
              <button type="button" className="btn-icone" onClick={() => void sair()} title="Terminar sessão" aria-label="Terminar sessão">
                <LogOut className="h-5 w-5" />
              </button>
            )}
          </div>
        </header>

        {estado !== 'ligado' && estado !== 'a-verificar' && (
          <div role="alert" className="bg-red-600 px-4 py-2 text-center text-sm font-medium text-white">
            {DESCRICAO_LIGACAO[estado]}. As operações que gravam dados estão bloqueadas até a ligação ser restabelecida.
          </div>
        )}
        {producao.dados && !producao.dados.emProducao && (
          <div role="status" className="flex flex-wrap items-center justify-center gap-x-3 bg-amber-100 px-4 py-1.5 text-center text-sm font-medium text-amber-900">
            <span>Modo de testes: as vendas e os documentos podem ser apagados antes de iniciar a produção.</span>
            {tem('configuracoes.gerir') && <NavLink to="/configuracoes?aba=sistema" className="underline">Iniciar produção</NavLink>}
          </div>
        )}
        {licenca && licenca.estado !== 'Valida' && (
          <div role="alert" className={`flex flex-wrap items-center justify-center gap-x-3 px-4 py-2 text-center text-sm font-medium ${licenca.bloqueada || licenca.estado === 'Tolerancia' ? 'bg-red-600 text-white' : 'bg-amber-100 text-amber-900'}`}>
            <span>{licenca.mensagem}</span>
            {tem('configuracoes.gerir') && (
              <NavLink to="/configuracoes?aba=licenca" className="underline">Activar licença</NavLink>
            )}
          </div>
        )}

        <main className="flex-1 overflow-y-auto p-4 lg:p-6">
          <Outlet />
        </main>
        <VisualizadorPdf />
      </div>
    </div>
  )
}

const CHAVE_MENU = 'lamgest.menuRecolhido'

/** Menu lateral só com ícones: escolha guardada neste computador; por omissão recolhido em ecrãs com menos de 1280 px (ex.: 1024x768). */
function lerMenuRecolhido(): boolean {
  try {
    const v = localStorage.getItem(CHAVE_MENU)
    if (v !== null) return v === '1'
  } catch { /* sem armazenamento */ }
  return window.innerWidth < 1280
}

function guardarMenuRecolhido(recolhido: boolean) {
  try { localStorage.setItem(CHAVE_MENU, recolhido ? '1' : '0') } catch { /* sem armazenamento */ }
}

/** Teclado no ecrã deste posto (guardado neste computador/tablet): Automático (só ecrãs tácteis), Sempre ou Nunca. */
/** Botão ☀/☾: alterna o modo claro/escuro neste computador (os documentos impressos ficam sempre claros). */
function BotaoTema() {
  const { escuro, alternar } = useTema()
  const texto = escuro ? 'Mudar para o modo claro' : 'Mudar para o modo escuro'
  return (
    <button type="button" className="btn-icone" onClick={alternar} title={texto} aria-label={texto}>
      {escuro ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
    </button>
  )
}

function ModoTecladoEcra() {
  const [modo, setModo] = useState<ModoTeclado>(lerModoTeclado)
  return (
    <label className="hidden items-center gap-1 text-xs text-slate-600 sm:flex" title="Teclado no ecrã deste posto (para ecrãs tácteis sem teclado)">
      <Keyboard className="h-5 w-5 text-slate-500" aria-hidden />
      <span className="sr-only">Teclado no ecrã</span>
      <select className="rounded-md border border-slate-300 bg-superficie py-1 pl-1 pr-6 text-xs" value={modo} data-sem-teclado
        onChange={(e) => { const m = e.target.value as ModoTeclado; setModo(m); guardarModoTeclado(m) }}>
        {(Object.keys(NOMES_MODO_TECLADO) as ModoTeclado[]).map((m) => <option key={m} value={m}>Teclado: {NOMES_MODO_TECLADO[m]}</option>)}
      </select>
    </label>
  )
}
