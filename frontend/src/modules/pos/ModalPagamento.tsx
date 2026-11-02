import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { api, ErroApi, novaChaveIdempotencia } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { MetodoPagamento, Pedido, SessaoCaixa } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { lerTerminalLocal } from '../../utils/terminalLocal'

/**
 * Pagamento (total ou parcial). Os valores são validados no servidor; aqui só se ajuda o operador.
 * A chave de idempotência é criada uma vez por tentativa: se a resposta se perder e o operador
 * carregar de novo, o servidor reconhece a chave e não duplica o pagamento.
 */
export function ModalPagamento({ pedido, aoPagar, aoFechar }: { pedido: Pedido; aoPagar: (p: Pedido) => void; aoFechar: () => void }) {
  const { podeGravar } = useLigacao()
  const terminalId = lerTerminalLocal()
  const sessao = useDados(
    () => (terminalId ? api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`) : Promise.resolve(undefined)),
    [terminalId],
  )
  const { dados: metodos } = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [valor, setValor] = useState(pedido.emDivida.toFixed(2))
  const [recebido, setRecebido] = useState('')
  const [referencia, setReferencia] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aPagar, setAPagar] = useState(false)
  const [chave, setChave] = useState(novaChaveIdempotencia)

  const activos = useMemo(() => metodos?.filter((m) => m.activo) ?? [], [metodos])
  useEffect(() => {
    if (metodoId === null && activos.length > 0) setMetodoId(activos[0].id)
  }, [activos, metodoId])
  const metodo = activos.find((m) => m.id === metodoId) ?? null

  // Ao mudar os dados do pagamento é uma nova operação: nova chave.
  useEffect(() => setChave(novaChaveIdempotencia()), [metodoId, valor, recebido, referencia])

  const v = Number(valor)
  const r = recebido === '' ? v : Number(recebido)
  const troco = Number.isFinite(r) && Number.isFinite(v) ? Math.round((r - v) * 100) / 100 : 0
  const erroLocal =
    !Number.isFinite(v) || v <= 0 ? 'Indique o valor a pagar.'
      : v > pedido.emDivida ? `O valor excede o saldo em dívida (${formatarMoeda(pedido.emDivida)}).`
        : !Number.isFinite(r) || r < v ? 'O valor recebido é inferior ao valor a pagar.'
          : metodo && !metodo.permiteTroco && r > v ? `${metodo.nome} não permite troco.`
            : metodo?.exigeReferencia && !referencia.trim() ? `Indique a referência ${metodo.nome}.`
              : null

  const pagar = async () => {
    if (!sessao.dados || !metodo || erroLocal) return
    setErro(null)
    setAPagar(true)
    try {
      const actualizado = await api.post<Pedido>(
        `/api/pedidos/${pedido.id}/pagamentos`,
        { sessaoCaixaId: sessao.dados.id, metodoPagamentoId: metodo.id, valor: v, valorRecebido: r, referencia: referencia.trim() || null },
        { 'Idempotency-Key': chave },
      )
      aoPagar(actualizado)
    } catch (e) {
      // Erro de rede: a chave mantém-se, para que repetir não duplique o pagamento.
      setErro(e instanceof ErroApi && e.semLigacao ? `${e.message} Pode tentar de novo sem risco de pagar duas vezes.` : mensagemErro(e))
    } finally {
      setAPagar(false)
    }
  }

  const semCaixa = !terminalId || (!sessao.aCarregar && !sessao.dados)

  return (
    <Modal titulo={`Pagamento · ${pedido.numero}`} aoFechar={aoFechar} largura="max-w-xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario min-w-40" onClick={() => void pagar()} disabled={aPagar || !podeGravar || !!erroLocal || semCaixa || !sessao.dados}>
          {aPagar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Confirmar {formatarMoeda(Number.isFinite(v) ? v : 0)}
        </button>
      </>}>
      {sessao.aCarregar ? <Carregando /> : semCaixa ? (
        <Alerta tipo="aviso" titulo="Caixa não aberto">
          {terminalId ? 'Não há sessão de caixa aberta neste terminal.' : 'Este computador ainda não está associado a um terminal de caixa.'}{' '}
          <Link to="/caixa" className="font-medium underline">Ir para o Caixa</Link>
        </Alerta>
      ) : (
        <div className="space-y-4">
          {sessao.erro && <Alerta tipo="erro">{sessao.erro}</Alerta>}
          {erro && <Alerta tipo="erro">{erro}</Alerta>}
          <div className="grid grid-cols-3 gap-2 rounded-lg bg-slate-50 p-3 text-center text-sm">
            <div><p className="text-slate-500">Total</p><p className="font-semibold">{formatarMoeda(pedido.total)}</p></div>
            <div><p className="text-slate-500">Pago</p><p className="font-semibold">{formatarMoeda(pedido.totalPago)}</p></div>
            <div><p className="text-slate-500">Em dívida</p><p className="text-lg font-bold text-primaria-700">{formatarMoeda(pedido.emDivida)}</p></div>
          </div>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            {activos.map((m) => (
              <button key={m.id} type="button" onClick={() => setMetodoId(m.id)} className={`${metodoId === m.id ? 'btn-primario' : 'btn-secundario'} min-h-14`}>
                {m.nome}
              </button>
            ))}
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="valorPagar" className="rotulo">Valor a pagar (MT)</label>
              <input id="valorPagar" type="number" inputMode="decimal" min={0} step="0.01" className="campo text-lg" value={valor} onChange={(e) => setValor(e.target.value)} />
              <p className="mt-1 text-xs text-slate-500">Para dividir a conta, pague só uma parte.</p>
            </div>
            {metodo?.permiteTroco && (
              <div>
                <label htmlFor="valorRecebido" className="rotulo">Valor recebido (MT)</label>
                <input id="valorRecebido" type="number" inputMode="decimal" min={0} step="0.01" className="campo text-lg" value={recebido}
                  onChange={(e) => setRecebido(e.target.value)} placeholder={valor} />
                <div className="mt-2 flex flex-wrap gap-1">
                  {[100, 200, 500, 1000, 2000].filter((n) => n >= v).slice(0, 4).map((n) => (
                    <button key={n} type="button" className="btn-secundario min-h-9 px-3 text-xs" onClick={() => setRecebido(String(n))}>{n}</button>
                  ))}
                </div>
              </div>
            )}
            {metodo?.exigeReferencia && (
              <div>
                <label htmlFor="referencia" className="rotulo">Referência da transacção</label>
                <input id="referencia" className="campo" maxLength={100} value={referencia} onChange={(e) => setReferencia(e.target.value)} />
              </div>
            )}
          </div>
          {troco > 0 && !erroLocal && (
            <div className="rounded-lg bg-green-50 p-3 text-center text-green-800">
              Troco: <span className="text-2xl font-bold">{formatarMoeda(troco)}</span>
            </div>
          )}
          {erroLocal && <p className="text-sm text-amber-700">{erroLocal}</p>}
          {sessao.dados && <p className="text-xs text-slate-500">Sessão de caixa n.º {sessao.dados.id} · {sessao.dados.terminal}</p>}
        </div>
      )}
    </Modal>
  )
}
