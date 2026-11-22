import { AlertTriangle, CheckCircle2, Database, HardDrive, Lock, RefreshCw, ShieldCheck } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { EstadoSistema } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'

const gb = (mb: number) =>
  mb < 1024
    ? `${Math.round(mb).toLocaleString('pt-PT')} MB`
    : `${(mb / 1024).toLocaleString('pt-PT', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} GB`

/** Configurações → Sistema: cópias de segurança e espaço do servidor (com os mesmos avisos da faixa do topo). */
export function EstadoServidor() {
  const estado = useDados(() => api.get<EstadoSistema>('/api/sistema/estado'), [])

  if (estado.erro) return <Alerta tipo="erro">{estado.erro}</Alerta>
  if (!estado.dados) return <Carregando />
  const { copias: c, espaco: e, avisos, baseDados, certificado } = estado.dados
  const usoBase = e.limiteMb ? Math.min(100, (e.baseMb / e.limiteMb) * 100) : null
  const usoDisco = e.discoLivreMb != null && e.discoTotalMb ? Math.min(100, 100 - (e.discoLivreMb / e.discoTotalMb) * 100) : null

  return (
    <section className="cartao space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-lg font-semibold"><ShieldCheck className="h-5 w-5 text-primaria-600" aria-hidden /> Estado do servidor</h2>
        <button type="button" className="btn-secundario" onClick={() => void estado.recarregar()}>
          <RefreshCw className="h-4 w-4" aria-hidden /> Actualizar
        </button>
      </div>

      {avisos.length === 0 ? (
        <Alerta tipo="sucesso">Cópias de segurança em dia e espaço suficiente.</Alerta>
      ) : (
        avisos.map((a) => (
          <Alerta key={a.titulo} tipo={a.nivel === 'critico' ? 'erro' : 'aviso'} titulo={a.titulo}>{a.texto}</Alerta>
        ))
      )}

      <div className="grid gap-4 md:grid-cols-3">
        <div className="space-y-1 rounded-lg border border-slate-200 p-3">
          <p className="flex items-center gap-2 font-medium">
            {c.ultimaComSucesso === false
              ? <AlertTriangle className="h-4 w-4 text-red-600" aria-hidden />
              : <CheckCircle2 className="h-4 w-4 text-green-600" aria-hidden />}
            Cópias de segurança
          </p>
          <p className="text-sm text-slate-600">Última cópia: <strong>{c.ultimaCopiaUtc ? formatarDataHora(c.ultimaCopiaUtc) : 'sem registo'}</strong></p>
          {c.ultimaExecucaoUtc && (
            <p className="text-sm text-slate-600">
              Última execução da tarefa: {formatarDataHora(c.ultimaExecucaoUtc)} · {c.ultimaComSucesso ? 'correu bem' : 'falhou'}
            </p>
          )}
          {c.mensagem && <p className="break-words text-xs text-slate-500">{c.mensagem}</p>}
        </div>

        <div className="space-y-1 rounded-lg border border-slate-200 p-3">
          <p className="flex items-center gap-2 font-medium"><Database className="h-4 w-4 text-primaria-600" aria-hidden /> Base de dados</p>
          <p className="text-sm text-slate-600"><span translate="no">{baseDados}</span>: <strong>{gb(e.baseMb)}</strong>{e.limiteMb ? <>{' de '}{gb(e.limiteMb)}</> : null}</p>
          {usoBase != null && <Barra percentagem={usoBase} aviso={70} critico={90} />}
          <p className="text-xs text-slate-500">{e.limiteMb ? 'SQL Server Express: limite de 10 GB por base.' : 'Edição do SQL Server sem limite de tamanho.'}</p>
        </div>

        <div className="space-y-1 rounded-lg border border-slate-200 p-3">
          <p className="flex items-center gap-2 font-medium"><HardDrive className="h-4 w-4 text-primaria-600" aria-hidden /> Disco da base</p>
          {e.discoLivreMb != null ? (
            <>
              <p className="text-sm text-slate-600">{e.disco} · <strong>{gb(e.discoLivreMb)}</strong> livres{e.discoTotalMb ? <>{' de '}{gb(e.discoTotalMb)}</> : null}</p>
              {usoDisco != null && <Barra percentagem={usoDisco} aviso={90} critico={97} />}
            </>
          ) : (
            <p className="text-sm text-slate-600">O SQL Server está noutro computador: veja o espaço nesse computador.</p>
          )}
        </div>
      </div>

      <p className="flex flex-wrap items-center gap-2 text-sm text-slate-600">
        <Lock className="h-4 w-4 text-primaria-600" aria-hidden />
        {certificado ? (
          <>
            <span>Ligação cifrada (HTTPS) activa. Certificado válido até</span>
            <strong>{formatarDataHora(certificado.validoAteUtc)}</strong>
            <a className="text-primaria-700 underline" href="/api/sistema/certificado.cer">Descarregar a autoridade local (.cer)</a>
          </>
        ) : (
          <span>Sem ligação cifrada (HTTPS): o servidor atende só em HTTP. Active-a no LAMGest-Setup (modo Servidor).</span>
        )}
      </p>
    </section>
  )
}

/** Cores iguais aos avisos do servidor: âmbar a partir de «aviso» %, vermelho a partir de «critico» %. */
function Barra({ percentagem, aviso, critico }: { percentagem: number; aviso: number; critico: number }) {
  const cor = percentagem >= critico ? 'bg-red-600' : percentagem >= aviso ? 'bg-amber-500' : 'bg-green-600'
  return (
    <div className="h-2 overflow-hidden rounded-full bg-slate-200" role="meter" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(percentagem)}
      aria-label={`${Math.round(percentagem)} % ocupado`}>
      <div className={`h-full ${cor}`} style={{ width: `${percentagem}%` }} />
    </div>
  )
}
