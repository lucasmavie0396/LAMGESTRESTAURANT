import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Loader2, Plus, Trash2 } from 'lucide-react'
import { api, ErroApi, novaChaveIdempotencia } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { CartaoEvento, MetodoPagamento, Pedido, SessaoCaixa } from '../../types/api'
import { imprimeNoPda } from '../../utils/pda'
import { formatarMoeda } from '../../utils/formatar'
import { lerTerminalLocal } from '../../utils/terminalLocal'

interface Linha {
  chave: number
  metodoId: number | null
  valor: string
  recebido: string
  referencia: string
  /** Cartão Evento lido (saldo no momento da leitura). */
  cartao?: CartaoEvento | null
}

const MAXIMO_LINHAS = 10
const centavos = (v: number) => Math.round(v * 100) / 100

/**
 * Pagamento total ou parcial, com um ou vários métodos de uma vez (ex.: parte em numerário, parte por M-Pesa).
 * Os valores são validados no servidor, que regista tudo ou nada; aqui só se ajuda o operador.
 * A chave de idempotência é criada uma vez por tentativa: se a resposta se perder e o operador
 * carregar de novo, o servidor reconhece a chave e não duplica os pagamentos.
 */
export function ModalPagamento({ pedido, aoPagar, aoFechar }: { pedido: Pedido; aoPagar: (p: Pedido) => void; aoFechar: () => void }) {
  const { podeGravar } = useLigacao()
  const terminalId = lerTerminalLocal()
  const sessao = useDados(
    () => (terminalId ? api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`) : Promise.resolve(undefined)),
    [terminalId],
  )
  const { dados: metodos } = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  const [linhas, setLinhas] = useState<Linha[]>([{ chave: 1, metodoId: null, valor: pedido.emDivida.toFixed(2), recebido: '', referencia: '' }])
  const [erro, setErro] = useState<string | null>(null)
  const [aPagar, setAPagar] = useState(false)
  const [chave, setChave] = useState(novaChaveIdempotencia)

  const activos = useMemo(() => metodos?.filter((m) => m.activo) ?? [], [metodos])
  useEffect(() => {
    if (activos.length > 0) setLinhas((ls) => ls.map((l) => (l.metodoId === null ? { ...l, metodoId: activos[0].id } : l)))
  }, [activos])

  // Ao mudar os dados do pagamento é uma nova operação: nova chave.
  useEffect(() => setChave(novaChaveIdempotencia()), [linhas])

  const alterar = (chaveLinha: number, dados: Partial<Linha>) => setLinhas((ls) => ls.map((l) => (l.chave === chaveLinha ? { ...l, ...dados } : l)))

  const calculadas = linhas.map((l) => {
    const metodo = activos.find((m) => m.id === l.metodoId) ?? null
    const v = Number(l.valor)
    const r = l.recebido === '' || !metodo?.permiteTroco ? v : Number(l.recebido)
    const troco = Number.isFinite(r) && Number.isFinite(v) ? centavos(r - v) : 0
    const cartaoEvento = metodo?.codigo === 'CARTAO_EVENTO'
    const erroLinha =
      !metodo ? 'Escolha o método.'
        : cartaoEvento && !l.cartao ? 'Passe o Cartão Evento (ou escreva o número).'
        : cartaoEvento && !l.cartao!.activo ? `O cartão ${l.cartao!.numero} está bloqueado.`
        : !Number.isFinite(v) || v <= 0 ? 'Indique o valor.'
        : cartaoEvento && v > l.cartao!.saldo ? `Saldo insuficiente no cartão: ${formatarMoeda(l.cartao!.saldo)}.`
          : !Number.isFinite(r) || r < v ? 'O valor recebido é inferior ao valor a pagar.'
            : metodo.exigeReferencia && !l.referencia.trim() ? `Indique a referência ${metodo.nome}.`
              : null
    return { ...l, metodo, v, r, troco, erroLinha, cartaoEvento }
  })
  const soma = centavos(calculadas.reduce((s, l) => s + (Number.isFinite(l.v) ? l.v : 0), 0))
  const resta = centavos(pedido.emDivida - soma)
  const trocoTotal = centavos(calculadas.reduce((s, l) => s + Math.max(l.troco, 0), 0))
  const erroLocal = calculadas.find((l) => l.erroLinha)?.erroLinha
    ?? (soma > pedido.emDivida ? `A soma (${formatarMoeda(soma)}) excede o saldo em dívida (${formatarMoeda(pedido.emDivida)}).` : null)

  const adicionarLinha = () => {
    const usados = new Set(linhas.map((l) => l.metodoId))
    const proximo = activos.find((m) => !usados.has(m.id)) ?? activos[0]
    setLinhas((ls) => [...ls, { chave: Math.max(...ls.map((l) => l.chave)) + 1, metodoId: proximo?.id ?? null, valor: resta > 0 ? resta.toFixed(2) : '', recebido: '', referencia: '' }])
  }

  const pagar = async () => {
    if (!sessao.dados || erroLocal) return
    setErro(null)
    setAPagar(true)
    try {
      const actualizado = await api.post<Pedido>(
        `/api/pedidos/${pedido.id}/pagamentos/multiplos`,
        {
          sessaoCaixaId: sessao.dados.id,
          linhas: calculadas.map((l) => ({ metodoPagamentoId: l.metodo!.id, valor: l.v, valorRecebido: l.r, referencia: (l.cartaoEvento ? l.cartao?.numero : l.referencia.trim()) || null })),
          reciboNoDispositivo: imprimeNoPda(),
        },
        { 'Idempotency-Key': chave },
      )
      aoPagar(actualizado)
    } catch (e) {
      // Erro de rede: a chave mantém-se, para que repetir não duplique os pagamentos.
      setErro(e instanceof ErroApi && e.semLigacao ? `${e.message} Pode tentar de novo sem risco de pagar duas vezes.` : mensagemErro(e))
    } finally {
      setAPagar(false)
    }
  }

  const semCaixa = !terminalId || (!sessao.aCarregar && !sessao.dados)

  return (
    <Modal titulo={`Pagamento · ${pedido.numero}`} aoFechar={aoFechar} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario min-w-40" onClick={() => void pagar()} disabled={aPagar || !podeGravar || !!erroLocal || semCaixa || !sessao.dados}>
          {aPagar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Confirmar {formatarMoeda(soma)}
        </button>
      </>}>
      {sessao.aCarregar ? <Carregando /> : semCaixa ? (
        <Alerta tipo="aviso" titulo="Caixa não aberto">
          {terminalId ? 'Ainda não abriu a sua sessão de caixa neste terminal (cada operador recebe na sua).' : 'Este computador ainda não está associado a um terminal de caixa.'}{' '}
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

          <ol className="space-y-3">
            {calculadas.map((l, n) => (
              <li key={l.chave} className="rounded-lg border border-slate-200 p-3" aria-label={`Pagamento ${n + 1}`}>
                <div className="mb-2 flex items-center justify-between">
                  <span className="text-sm font-semibold">{linhas.length > 1 ? `Pagamento ${n + 1}` : 'Método de pagamento'}</span>
                  {linhas.length > 1 && (
                    <button type="button" className="btn-icone h-9 w-9" aria-label={`Retirar pagamento ${n + 1}`} onClick={() => setLinhas((ls) => ls.filter((x) => x.chave !== l.chave))}>
                      <Trash2 className="h-4 w-4 text-red-600" />
                    </button>
                  )}
                </div>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  {activos.map((m) => (
                    <button key={m.id} type="button" onClick={() => alterar(l.chave, { metodoId: m.id, recebido: '', referencia: '', cartao: null })}
                      className={`${l.metodoId === m.id ? 'btn-primario' : 'btn-secundario'} min-h-12`} aria-pressed={l.metodoId === m.id}>
                      {m.nome}
                    </button>
                  ))}
                </div>
                <div className="mt-3 grid gap-3 sm:grid-cols-2">
                  <div>
                    <label htmlFor={`valor${l.chave}`} className="rotulo">Valor (MT)</label>
                    <input id={`valor${l.chave}`} type="number" inputMode="decimal" min={0} step="0.01" className="campo text-lg" value={l.valor}
                      onChange={(e) => alterar(l.chave, { valor: e.target.value })} />
                  </div>
                  {l.metodo?.permiteTroco && (
                    <div>
                      <label htmlFor={`recebido${l.chave}`} className="rotulo">Valor recebido (MT)</label>
                      <input id={`recebido${l.chave}`} type="number" inputMode="decimal" min={0} step="0.01" className="campo text-lg" value={l.recebido}
                        onChange={(e) => alterar(l.chave, { recebido: e.target.value })} placeholder={l.valor} />
                      <div className="mt-2 flex flex-wrap gap-1">
                        {[100, 200, 500, 1000, 2000].filter((x) => x >= l.v).slice(0, 4).map((x) => (
                          <button key={x} type="button" className="btn-secundario min-h-9 px-3 text-xs" onClick={() => alterar(l.chave, { recebido: String(x) })}>{x}</button>
                        ))}
                      </div>
                    </div>
                  )}
                  {l.cartaoEvento && (
                    <LerCartao cartao={l.cartao ?? null} aoLer={(c) => alterar(l.chave, { cartao: c, valor: c ? Math.min(Number(l.valor) || 0, c.saldo).toFixed(2) : l.valor })} />
                  )}
                  {l.metodo?.exigeReferencia && !l.cartaoEvento && (
                    <div>
                      <label htmlFor={`referencia${l.chave}`} className="rotulo">Referência da transacção</label>
                      <input id={`referencia${l.chave}`} className="campo" maxLength={100} value={l.referencia} onChange={(e) => alterar(l.chave, { referencia: e.target.value })} />
                    </div>
                  )}
                </div>
                {l.troco > 0 && !l.erroLinha && <p className="mt-2 text-sm font-medium text-green-800">Troco: {formatarMoeda(l.troco)}</p>}
                {l.erroLinha && linhas.length > 1 && <p className="mt-2 text-sm text-amber-700">{l.erroLinha}</p>}
              </li>
            ))}
          </ol>

          <button type="button" className="btn-secundario w-full" onClick={adicionarLinha} disabled={linhas.length >= MAXIMO_LINHAS || activos.length === 0}>
            <Plus className="h-4 w-4" aria-hidden /> Juntar outro método de pagamento
          </button>

          <div className="grid grid-cols-3 gap-2 rounded-lg bg-slate-50 p-3 text-center text-sm">
            <div><p className="text-slate-500">A pagar agora</p><p className="font-semibold">{formatarMoeda(soma)}</p></div>
            <div><p className="text-slate-500">Fica em dívida</p><p className={`font-semibold ${resta > 0 ? 'text-amber-700' : ''}`}>{formatarMoeda(Math.max(resta, 0))}</p></div>
            <div><p className="text-slate-500">Troco</p><p className="text-lg font-bold text-green-700">{formatarMoeda(trocoTotal)}</p></div>
          </div>
          {erroLocal && <p className="text-sm text-amber-700">{erroLocal}</p>}
          <p className="text-xs text-slate-500">Para dividir a conta, pague só uma parte; o resto pode ser pago depois.</p>
          {sessao.dados && <p className="text-xs text-slate-500">Sessão de caixa n.º {sessao.dados.id} · {sessao.dados.terminal}</p>}
        </div>
      )}
    </Modal>
  )
}

/** Leitura do Cartão Evento (magnético, código de barras ou QR code): mostra o saldo antes de confirmar. */
function LerCartao({ cartao, aoLer }: { cartao: CartaoEvento | null; aoLer: (c: CartaoEvento | null) => void }) {
  const [lido, setLido] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aLer, setALer] = useState(false)
  const ler = async () => {
    if (!lido.trim()) return
    setALer(true)
    setErro(null)
    try {
      aoLer(await api.get<CartaoEvento>(`/api/cartoes/consulta?numero=${encodeURIComponent(lido)}`))
      setLido('')
    } catch (e) {
      setErro(mensagemErro(e))
      aoLer(null)
    } finally {
      setALer(false)
    }
  }
  return (
    <div>
      <label htmlFor="lerCartao" className="rotulo">Cartão Evento</label>
      <input id="lerCartao" className="campo" autoFocus autoComplete="off" placeholder="Passe o cartão ou leia o código e Enter" value={lido}
        onChange={(e) => setLido(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void ler() } }} onBlur={() => void ler()} />
      {aLer && <p className="mt-1 text-xs text-slate-500">A ler…</p>}
      {erro && <p className="mt-1 text-xs text-red-700">{erro}</p>}
      {cartao && <p className="mt-1 text-sm"><strong className="tabular-nums">{cartao.numero}</strong>{cartao.titular && ` · ${cartao.titular}`} · saldo <strong className="text-primaria-700">{formatarMoeda(cartao.saldo)}</strong></p>}
    </div>
  )
}
