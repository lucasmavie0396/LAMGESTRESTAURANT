import { Link } from 'react-router-dom'
import { ShieldCheck, Users } from 'lucide-react'
import { Alerta } from '../components/ui'
import { useSessao } from '../contexts/SessaoContext'
import { P } from '../utils/permissoes'

/**
 * Página inicial. Os indicadores do dashboard (vendas, pedidos, caixa, stock) serão calculados
 * a partir dos dados reais quando os módulos de vendas e caixa estiverem implementados (Fase 3).
 * Até lá não se mostram números — nada de dados fictícios.
 */
export function Inicio() {
  const { utilizador, tem } = useSessao()
  const atalhos = [
    tem(P.utilizadoresGerir) && { rota: '/utilizadores', rotulo: 'Gerir utilizadores', icone: Users },
    tem(P.perfisGerir) && { rota: '/perfis', rotulo: 'Perfis e permissões', icone: ShieldCheck },
  ].filter((a): a is { rota: string; rotulo: string; icone: typeof Users } => Boolean(a))

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Bem-vindo, {utilizador?.nomeCompleto}</h1>
        <p className="text-slate-600">LAMGest Restaurant ERP</p>
      </div>

      <Alerta tipo="info" titulo="Sistema em implementação">
        Os indicadores do dashboard (vendas do dia, pedidos, caixa, stock baixo) ficam disponíveis quando os módulos de POS, caixa e
        stock estiverem concluídos. Os módulos marcados como «Brevemente» no menu ainda não estão activos.
      </Alerta>

      {atalhos.length > 0 && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {atalhos.map((a) => (
            <Link key={a.rota} to={a.rota} className="cartao flex items-center gap-4 p-5 hover:border-primaria-300 hover:shadow">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primaria-100 text-primaria-700">
                <a.icone className="h-6 w-6" aria-hidden />
              </div>
              <span className="font-medium">{a.rotulo}</span>
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}
