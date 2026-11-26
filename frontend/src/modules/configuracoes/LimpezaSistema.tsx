import { useState } from 'react'
import { Eraser, Loader2, RefreshCw, ShieldAlert } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { EstadoLimpeza, ItemLimpeza, ResultadoLimpeza, TipoLimpeza } from '../../types/api'

const mb = (v: number) => (v < 1024 ? `${v.toLocaleString('pt-PT', { maximumFractionDigits: 1 })} MB` : `${(v / 1024).toLocaleString('pt-PT', { maximumFractionDigits: 2 })} GB`)

/** Regra de cada limpeza: o que o número quer dizer. */
const REGRA: Partial<Record<TipoLimpeza, { rotulo: string; opcoes: number[] }>> = {
  TrabalhosImpressao: { rotulo: 'Apagar os impressos há mais de', opcoes: [7, 15, 30, 60, 90] },
  Logs: { rotulo: 'Apagar os ficheiros com mais de', opcoes: [7, 15, 30, 60, 90] },
  Copias: { rotulo: 'Manter as cópias mais recentes:', opcoes: [7, 14, 30, 60] },
}

/**
 * Configurações → Sistema → Limpeza e espaço: o que ocupa a base e o que se pode apagar sem prejudicar o sistema (dados
 * técnicos). Mostra quanto apaga e liberta antes de confirmar; registos de negócio e auditoria nunca se apagam.
 */
export function LimpezaSistema() {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [regras, setRegras] = useState({ diasTrabalhos: 30, diasLogs: 30, manterCopias: 14 })
  const estado = useDados(() => api.get<EstadoLimpeza>('/api/sistema/limpeza' + query(regras)), [regras.diasTrabalhos, regras.diasLogs, regras.manterCopias])
  const [aLimpar, setALimpar] = useState<TipoLimpeza | null>(null)

  const valorDe = (t: TipoLimpeza) => (t === 'TrabalhosImpressao' ? regras.diasTrabalhos : t === 'Logs' ? regras.diasLogs : t === 'Copias' ? regras.manterCopias : 0)
  const mudar = (t: TipoLimpeza, v: number) =>
    setRegras((r) => (t === 'TrabalhosImpressao' ? { ...r, diasTrabalhos: v } : t === 'Logs' ? { ...r, diasLogs: v } : { ...r, manterCopias: v }))

  const limpar = async (item: ItemLimpeza) => {
    if (!window.confirm(`${item.titulo}: apagar ${item.quantidade} ${item.tipo === 'RegistoTransaccoes' ? 'ficheiro (reduzir)' : 'elementos'} e libertar cerca de ${mb(item.mb)}? Não se pode desfazer.`)) return
    setALimpar(item.tipo)
    try {
      const r = await api.post<ResultadoLimpeza>('/api/sistema/limpeza', { tipo: item.tipo, valor: valorDe(item.tipo) })
      avisos.sucesso(`${r.mensagem} Libertados ${mb(r.mbLibertados)}.`)
      await estado.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setALimpar(null)
    }
  }

  const e = estado.dados
  return (
    <section className="cartao space-y-4 p-4" aria-label="Limpeza e espaço">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-lg font-semibold"><Eraser className="h-5 w-5 text-primaria-600" aria-hidden /> Limpeza e espaço</h2>
        <button type="button" className="btn-secundario" onClick={() => void estado.recarregar()}><RefreshCw className="h-4 w-4" aria-hidden /> Actualizar</button>
      </div>
      <Alerta tipo="info" titulo="Só dados técnicos">
        Apaga apenas o que já não serve ao negócio. Pedidos, pagamentos, documentos fiscais, movimentos de caixa, stock e cartões e a auditoria
        nunca se apagam. Cada limpeza fica registada na auditoria.
      </Alerta>
      {estado.erro && <Alerta tipo="erro">{estado.erro}</Alerta>}
      {!e && !estado.erro && <Carregando />}
      {e && (
        <>
          <p className="text-sm text-slate-600">
            Base de dados: <strong>{mb(e.baseMb)}</strong>{e.limiteMb ? <> de {mb(e.limiteMb)} (limite do SQL Server Express)</> : null}
            {e.registoTransaccoesMb != null && <> · registo de transacções {mb(e.registoTransaccoesMb)}</>}
          </p>
          <ul className="space-y-3">
            {e.itens.map((i) => {
              const regra = REGRA[i.tipo]
              return (
                <li key={i.tipo} className="rounded-lg border border-slate-200 p-3">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-64 flex-1">
                      <p className="font-medium">{i.titulo}</p>
                      <p className="text-sm text-slate-600">{i.descricao}</p>
                      {i.local && <p className="text-xs text-slate-500">Pasta: {i.local}</p>}
                      {regra && (
                        <label className="mt-2 flex flex-wrap items-center gap-2 text-sm">
                          {regra.rotulo}
                          <select className="campo w-auto" value={i.valor} onChange={(x) => mudar(i.tipo, Number(x.target.value))}>
                            {regra.opcoes.map((o) => <option key={o} value={o}>{o} {i.tipo === 'Copias' ? 'cópias' : 'dias'}</option>)}
                          </select>
                        </label>
                      )}
                      {i.motivo && <p className="mt-1 text-sm text-amber-700">{i.motivo}</p>}
                    </div>
                    <div className="text-right">
                      <p className="text-sm text-slate-600">{i.quantidade === 0 ? 'Nada a limpar' : `${i.quantidade} · ${mb(i.mb)}`}</p>
                      <button type="button" className="btn-secundario mt-1" disabled={!podeGravar || !i.disponivel || i.quantidade === 0 || aLimpar !== null}
                        onClick={() => void limpar(i)}>
                        {aLimpar === i.tipo ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Eraser className="h-4 w-4" aria-hidden />}
                        {i.tipo === 'RegistoTransaccoes' ? 'Reduzir' : 'Limpar'}
                      </button>
                    </div>
                  </div>
                </li>
              )
            })}
          </ul>
          {e.tabelas.length > 0 && (
            <details className="text-sm">
              <summary className="cursor-pointer font-medium">Tabelas que mais ocupam</summary>
              <table className="mt-2 w-full max-w-xl text-left">
                <thead className="text-slate-600"><tr className="border-b border-slate-200">
                  <th scope="col" className="py-1">Tabela</th><th scope="col" className="py-1 text-right">Linhas</th><th scope="col" className="py-1 text-right">Espaço</th>
                </tr></thead>
                <tbody className="divide-y divide-slate-100 tabular-nums">
                  {e.tabelas.map((t) => (
                    <tr key={t.nome}><td className="py-1">{t.nome}</td><td className="py-1 text-right">{t.linhas.toLocaleString('pt-PT')}</td><td className="py-1 text-right">{mb(t.mb)}</td></tr>
                  ))}
                </tbody>
              </table>
              <p className="mt-2 flex items-start gap-1 text-xs text-slate-500">
                <ShieldAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
                Se as tabelas de vendas ou auditoria se aproximarem do limite, a solução é arquivar os anos antigos, nunca apagá-los.
              </p>
            </details>
          )}
        </>
      )}
    </section>
  )
}
