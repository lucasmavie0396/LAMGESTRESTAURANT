import { useEffect, useState } from 'react'
import { CheckCircle2, Loader2 } from 'lucide-react'
import { api, novaChaveIdempotencia } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Cliente, ContaCorrente, DocumentoComercial, FacturaEmDivida, MetodoPagamento, SessaoCaixa } from '../../types/api'
import { dataCurta } from '../../utils/datas'
import { formatarMoeda } from '../../utils/formatar'
import { metodoDeDinheiro } from '../../utils/metodos'
import { lerTerminalLocal } from '../../utils/terminalLocal'
import { SeletorCliente } from './FormDocumento'

const numero = (texto: string) => Number(texto.replace(',', '.'))
const centavos = (v: number) => Math.round(v * 100) / 100

/**
 * Pagamento de uma factura em dívida (gera o recibo): escolhe-se o cliente, a factura, se o pagamento é completo ou parcial
 * (no parcial escreve-se o valor pago, ao lado do valor em dívida) e a forma de pagamento.
 * Em dinheiro (gaveta) exige o caixa aberto neste terminal: o valor entra nessa sessão.
 */
export function FormRecibo({ clienteInicial, titulo = 'Pagamento de Fatura', aoFechar, aoEmitir }: {
  clienteInicial?: number
  titulo?: string
  aoFechar: () => void
  aoEmitir: (id: number) => void
}) {
  const { podeGravar } = useLigacao()
  const { tem } = useSessao()
  const [cliente, setCliente] = useState<Pick<Cliente, 'id' | 'nome' | 'nuit'> | null>(null)
  const clienteId = cliente?.id ?? clienteInicial
  const conta = useDados(() => (clienteId ? api.get<ContaCorrente>(`/api/documentos/conta-corrente/${clienteId}`) : Promise.resolve(null)), [clienteId])
  const metodos = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  const terminalId = lerTerminalLocal()
  const sessao = useDados(() => (terminalId && tem('caixa.operar') ? api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`) : Promise.resolve(undefined)), [terminalId])
  const [facturaId, setFacturaId] = useState<number | null>(null)
  const [modo, setModo] = useState<'completo' | 'parcial'>('completo')
  const [valorPago, setValorPago] = useState('')
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [referencia, setReferencia] = useState('')
  const [chave, setChave] = useState(novaChaveIdempotencia)
  const [erro, setErro] = useState<string | null>(null)
  const [aEmitir, setAEmitir] = useState(false)

  // Ao carregar a conta do cliente: com uma só factura em dívida, fica logo escolhida.
  useEffect(() => {
    if (conta.dados && !cliente) setCliente({ id: conta.dados.clienteId, nome: conta.dados.clienteNome, nuit: conta.dados.clienteNuit })
    setFacturaId(conta.dados?.facturas.length === 1 ? conta.dados.facturas[0].id : null)
  }, [conta.dados]) // eslint-disable-line react-hooks/exhaustive-deps
  // Outra escolha é outro pagamento: nova chave de idempotência.
  useEffect(() => setChave(novaChaveIdempotencia()), [facturaId, modo, valorPago, metodoId])

  const activos = metodos.dados?.filter((m) => m.activo && metodoDeDinheiro(m)) ?? []
  const metodo = activos.find((m) => m.id === (metodoId ?? activos[0]?.id))
  const factura: FacturaEmDivida | undefined = conta.dados?.facturas.find((f) => f.id === facturaId)
  const valor = !factura ? 0 : modo === 'completo' ? factura.emDivida : centavos(numero(valorPago))
  const semCaixa = metodo?.contaNaGaveta && !sessao.dados
  const erroValor = !factura || modo === 'completo' ? null
    : valorPago.trim() === '' ? 'Escreva o valor pago.'
      : !Number.isFinite(valor) || valor <= 0 ? 'Valor inválido.'
        : valor > factura.emDivida ? `O valor não pode exceder o que está em dívida (${formatarMoeda(factura.emDivida)}).`
          : null
  const pronto = !!factura && !!metodo && !erroValor && !semCaixa && (!metodo.exigeReferencia || referencia.trim().length > 0)

  const emitir = async () => {
    if (!clienteId || !metodo || !factura) return
    setErro(null)
    setAEmitir(true)
    try {
      const recibo = await api.post<DocumentoComercial>('/api/documentos/recibos', {
        clienteId, metodoPagamentoId: metodo.id, referencia: referencia.trim() || null, sessaoCaixaId: sessao.dados?.id ?? null, observacoes: null,
        liquidacoes: [{ facturaId: factura.id, valor }],
      }, { 'Idempotency-Key': chave })
      aoEmitir(recibo.id)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAEmitir(false)
    }
  }

  const escolherCliente = (c: Pick<Cliente, 'id' | 'nome' | 'nuit'> | null) => {
    setCliente(c)
    setFacturaId(null)
    setModo('completo')
    setValorPago('')
  }

  return (
    <Modal titulo={titulo} aoFechar={aoFechar} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aEmitir || !podeGravar || !pronto} onClick={() => void emitir()}>
          {aEmitir ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
          {modo === 'completo' ? 'Confirmar' : 'Finalizar'}{factura && !erroValor ? ` ${formatarMoeda(valor)}` : ''}
        </button>
      </>}>
      <div className="space-y-4 text-sm">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <SeletorCliente valor={cliente} aoMudar={escolherCliente} />
        {clienteId && !conta.dados && !conta.erro && <Carregando />}
        {conta.erro && <Alerta tipo="erro">{conta.erro}</Alerta>}
        {conta.dados && conta.dados.facturas.length === 0 && <Alerta tipo="info">Este cliente não tem facturas em dívida.</Alerta>}

        {conta.dados && conta.dados.facturas.length > 0 && (
          <>
            {/* 1. Factura */}
            <fieldset>
              <legend className="rotulo">Factura a pagar {conta.dados.facturas.length > 1 && <span className="font-normal text-slate-500">({conta.dados.facturas.length} em dívida · {formatarMoeda(conta.dados.totalEmDivida)})</span>}</legend>
              <div className="max-h-56 space-y-2 overflow-y-auto">
                {conta.dados.facturas.map((f) => (
                  <label key={f.id} className={`flex cursor-pointer items-center gap-3 rounded-lg border-2 px-3 py-2 ${facturaId === f.id ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
                    <input type="radio" name="factura" className="h-4 w-4" checked={facturaId === f.id} onChange={() => { setFacturaId(f.id); setValorPago('') }} />
                    <span className="flex-1">
                      <span className="font-medium">{f.numero}</span>
                      <span className={`block text-xs ${f.vencida ? 'font-medium text-red-700' : 'text-slate-500'}`}>
                        Total {formatarMoeda(f.total)}{f.vencimento && ` · vence ${dataCurta(f.vencimento)}`}{f.vencida && ' (vencida)'}
                      </span>
                    </span>
                    <span className="text-right"><span className="block text-xs text-slate-500">Em dívida</span><span className="font-semibold tabular-nums">{formatarMoeda(f.emDivida)}</span></span>
                  </label>
                ))}
              </div>
            </fieldset>

            {factura && (
              <>
                {/* 2. Completo ou parcial */}
                <div role="radiogroup" aria-label="Tipo de pagamento" className="grid grid-cols-2 gap-2">
                  {([['completo', 'Pagamento completo'], ['parcial', 'Pagamento parcial']] as const).map(([v, t]) => (
                    <button key={v} type="button" role="radio" aria-checked={modo === v} onClick={() => setModo(v)}
                      className={`min-h-12 rounded-lg font-semibold ${modo === v ? 'bg-primaria-600 text-white' : 'bg-white ring-1 ring-slate-300'}`}>{t}</button>
                  ))}
                </div>

                {/* 3. Valores */}
                {modo === 'completo' ? (
                  <div className="rounded-lg bg-slate-50 p-3 text-center">
                    <p className="text-slate-500">Valor a pagar ({factura.numero})</p>
                    <p className="text-2xl font-bold tabular-nums text-primaria-700">{formatarMoeda(factura.emDivida)}</p>
                  </div>
                ) : (
                  <div className="grid grid-cols-2 gap-3">
                    <div className="rounded-lg bg-slate-50 p-3">
                      <p className="text-slate-500">Valor em dívida</p>
                      <p className="text-2xl font-bold tabular-nums">{formatarMoeda(factura.emDivida)}</p>
                      {!erroValor && valor > 0 && <p className="mt-1 text-xs text-amber-700">Fica em dívida: {formatarMoeda(centavos(factura.emDivida - valor))}</p>}
                    </div>
                    <div>
                      <label htmlFor="rValorPago" className="rotulo">Valor pago (MT)</label>
                      <input id="rValorPago" className="campo text-lg" inputMode="decimal" autoFocus value={valorPago} placeholder="0,00" onChange={(e) => setValorPago(e.target.value)} />
                      {erroValor && valorPago.trim() !== '' && <p className="mt-1 text-xs text-red-700">{erroValor}</p>}
                    </div>
                  </div>
                )}

                {/* 4. Forma de pagamento */}
                <div>
                  <p className="rotulo">Forma de pagamento</p>
                  <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                    {activos.map((m) => (
                      <button key={m.id} type="button" aria-pressed={metodo?.id === m.id} onClick={() => { setMetodoId(m.id); setReferencia('') }}
                        className={`${metodo?.id === m.id ? 'btn-primario' : 'btn-secundario'} min-h-12`}>{m.nome}</button>
                    ))}
                  </div>
                </div>
                {metodo?.exigeReferencia && (
                  <div><label htmlFor="rRef" className="rotulo">Referência da transacção</label><input id="rRef" className="campo" maxLength={100} value={referencia} onChange={(e) => setReferencia(e.target.value)} /></div>
                )}
                {semCaixa
                  ? <Alerta tipo="aviso">Para receber em {metodo?.nome} abra o caixa neste terminal: o valor entra na sessão de caixa.</Alerta>
                  : sessao.dados && metodo?.contaNaGaveta && <p className="text-xs text-slate-500">Entra na sessão de caixa n.º {sessao.dados.id} deste terminal.</p>}
              </>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}
