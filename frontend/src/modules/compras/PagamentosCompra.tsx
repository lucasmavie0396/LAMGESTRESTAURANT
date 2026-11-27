import { useState } from 'react'
import { HandCoins, Loader2 } from 'lucide-react'
import { api, ErroApi, novaChaveIdempotencia } from '../../api/cliente'
import { Alerta, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Compra, MetodoPagamento, PagamentoCompra, SessaoCaixa } from '../../types/api'
import { hojeIso } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { lerTerminalLocal } from '../../utils/terminalLocal'
import { metodoDeDinheiro } from '../../utils/metodos'
import { DialogoMotivo } from '../pos/Dialogos'

/** Pagamentos ao fornecedor de uma compra confirmada: o que falta pagar, a lista e o registo de novos pagamentos. */
export function PagamentosCompra({ compra, aoMudar }: { compra: Compra; aoMudar: () => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const lista = useDados(() => api.get<PagamentoCompra[]>(`/api/compras/${compra.id}/pagamentos`), [compra.id, compra.pago])
  const [novo, setNovo] = useState(false)
  const [anular, setAnular] = useState<PagamentoCompra | null>(null)
  const emDivida = compra.emDivida ?? 0

  return (
    <section aria-label="Pagamentos ao fornecedor" className="rounded-lg border border-slate-200 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-semibold">Pagamentos ao fornecedor</h3>
          <p className="text-sm text-slate-600">
            Pago <span className="tabular-nums">{formatarMoeda(compra.pago ?? 0)}</span> · falta pagar{' '}
            <strong className={`tabular-nums ${emDivida > 0 ? 'text-red-700' : 'text-green-700'}`}>{formatarMoeda(emDivida)}</strong>
          </p>
        </div>
        {tem('compras.gerir') && emDivida > 0 && (
          <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setNovo(true)}>
            <HandCoins className="h-4 w-4" aria-hidden /> Registar pagamento
          </button>
        )}
      </div>
      {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
      {!!lista.dados?.length && (
        <ul className="mt-2 divide-y divide-slate-100 text-sm">
          {lista.dados.map((p) => (
            <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 py-1.5">
              <span className={p.estado === 'Anulado' ? 'text-slate-400 line-through' : ''}>
                {formatarDataHora(p.dataUtc)} · {p.metodo}{p.referencia && ` (${p.referencia})`}{p.sessaoCaixaId && ` · caixa n.º ${p.sessaoCaixaId}`}{p.utilizador && ` · ${p.utilizador}`}
              </span>
              <span className="flex items-center gap-2">
                <span className="font-medium tabular-nums">{formatarMoeda(p.valor)}</span>
                {p.estado === 'Anulado'
                  ? <Etiqueta cor="cinza">Anulado</Etiqueta>
                  : tem('compras.anular') && <button type="button" className="btn-secundario min-h-8 px-2 text-xs text-red-700" disabled={!podeGravar} onClick={() => setAnular(p)}>Anular</button>}
              </span>
            </li>
          ))}
        </ul>
      )}
      {novo && <NovoPagamento compra={compra} aoFechar={(ok) => { setNovo(false); if (ok) aoMudar() }} />}
      {anular && (
        <DialogoMotivo titulo={`Anular pagamento de ${formatarMoeda(anular.valor)}`} texto="Anular pagamento" aoFechar={() => setAnular(null)}
          descricao={anular.sessaoCaixaId ? 'Foi pago pelo caixa: se a sessão ainda estiver aberta, o valor volta ao caixa. Fica na auditoria.' : 'O valor volta a ficar em dívida. Fica na auditoria.'}
          aoConfirmar={async (motivo) => {
            try {
              await api.post(`/api/compras/pagamentos/${anular.id}/anular`, { motivo })
              avisos.sucesso('Pagamento anulado.')
              setAnular(null)
              aoMudar()
            } catch (e) {
              avisos.erro(mensagemErro(e))
            }
          }} />
      )}
    </section>
  )
}

function NovoPagamento({ compra, aoFechar }: { compra: Compra; aoFechar: (ok?: boolean) => void }) {
  const avisos = useAvisos()
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const metodos = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento')
    .then((l) => l.filter((m) => m.activo && metodoDeDinheiro(m))), [])
  const terminalId = lerTerminalLocal()
  const sessao = useDados(() => (terminalId && tem('caixa.operar') ? api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`) : Promise.resolve(undefined)), [terminalId])
  const [valor, setValor] = useState(String(compra.emDivida ?? ''))
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [referencia, setReferencia] = useState('')
  const [data, setData] = useState(hojeIso)
  const [pelaCaixa, setPelaCaixa] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const [chave] = useState(novaChaveIdempotencia)
  const metodo = metodoId ?? metodos.dados?.[0]?.id ?? null
  const v = Number(valor.replace(',', '.'))

  const guardar = async () => {
    if (!(v > 0)) return setErro('Indique um valor positivo.')
    setErro(null)
    setAGuardar(true)
    try {
      await api.post(`/api/compras/${compra.id}/pagamentos`, {
        valor: v, metodoPagamentoId: metodo, data: pelaCaixa ? null : data, referencia: referencia.trim() || null, sessaoCaixaId: pelaCaixa ? sessao.dados?.id : null,
      }, { 'Idempotency-Key': chave })
      avisos.sucesso(`Pagamento de ${formatarMoeda(v)} registado.`)
      aoFechar(true)
    } catch (e) {
      setErro(e instanceof ErroApi && e.semLigacao ? `${e.message} Pode tentar de novo sem risco de pagar duas vezes.` : mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={`Pagar a ${compra.fornecedor}`} aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aGuardar || !podeGravar || !metodo} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Registar {v > 0 ? formatarMoeda(v) : ''}
        </button>
      </>}>
      <div className="grid gap-4 sm:grid-cols-2">
        {erro && <div className="sm:col-span-2"><Alerta tipo="erro">{erro}</Alerta></div>}
        <p className="text-sm text-slate-600 sm:col-span-2">Compra {compra.numeroDocumento} · falta pagar <strong>{formatarMoeda(compra.emDivida ?? 0)}</strong></p>
        <div><label htmlFor="pcValor" className="rotulo">Valor (MT)</label><input id="pcValor" className="campo" inputMode="decimal" value={valor} onChange={(e) => setValor(e.target.value.trim())} /></div>
        <div>
          <label htmlFor="pcMetodo" className="rotulo">Pago por</label>
          <select id="pcMetodo" className="campo" value={metodo ?? ''} onChange={(e) => setMetodoId(Number(e.target.value))}>
            {metodos.dados?.map((m) => <option key={m.id} value={m.id}>{m.nome}</option>)}
          </select>
        </div>
        <div><label htmlFor="pcRef" className="rotulo">Referência (transferência, M-Pesa…)</label><input id="pcRef" className="campo" maxLength={100} value={referencia} onChange={(e) => setReferencia(e.target.value)} /></div>
        {!pelaCaixa && <div><label htmlFor="pcData" className="rotulo">Data</label><input id="pcData" type="date" className="campo" max={hojeIso()} value={data} onChange={(e) => setData(e.target.value)} /></div>}
        <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
          <input type="checkbox" className="h-4 w-4" checked={pelaCaixa} disabled={!sessao.dados} onChange={(e) => setPelaCaixa(e.target.checked)} />
          {sessao.dados ? `Pago com dinheiro da caixa deste terminal (sessão n.º ${sessao.dados.id})` : 'Pago pela caixa: indisponível (sem caixa aberto neste terminal)'}
        </label>
      </div>
    </Modal>
  )
}
