import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { api, novaChaveIdempotencia } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Cliente, ContaCorrente, DocumentoComercial, MetodoPagamento, SessaoCaixa } from '../../types/api'
import { dataCurta } from '../../utils/datas'
import { formatarMoeda } from '../../utils/formatar'
import { metodoDeDinheiro } from '../../utils/metodos'
import { lerTerminalLocal } from '../../utils/terminalLocal'
import { SeletorCliente } from './FormDocumento'

const numero = (texto: string) => Number(texto.replace(',', '.'))

/**
 * Recibo: paga uma ou mais facturas em dívida do cliente (total ou parcialmente).
 * Em dinheiro (gaveta) exige o caixa aberto neste terminal: o valor entra nessa sessão.
 */
export function FormRecibo({ clienteInicial, titulo = 'Novo recibo', aoFechar, aoEmitir }: { clienteInicial?: number; titulo?: string; aoFechar: () => void; aoEmitir: (id: number) => void }) {
  const { podeGravar } = useLigacao()
  const { tem } = useSessao()
  const [cliente, setCliente] = useState<Pick<Cliente, 'id' | 'nome' | 'nuit'> | null>(null)
  const clienteId = cliente?.id ?? clienteInicial
  const conta = useDados(() => (clienteId ? api.get<ContaCorrente>(`/api/documentos/conta-corrente/${clienteId}`) : Promise.resolve(null)), [clienteId])
  const metodos = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  const terminalId = lerTerminalLocal()
  const sessao = useDados(() => (terminalId && tem('caixa.operar') ? api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`) : Promise.resolve(undefined)), [terminalId])
  const [valores, setValores] = useState<Record<number, string>>({})
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [referencia, setReferencia] = useState('')
  const [chave] = useState(novaChaveIdempotencia)
  const [erro, setErro] = useState<string | null>(null)
  const [aEmitir, setAEmitir] = useState(false)

  // Ao escolher o cliente, propõe-se pagar tudo o que está em dívida.
  useEffect(() => {
    if (conta.dados) setValores(Object.fromEntries(conta.dados.facturas.map((f) => [f.id, f.emDivida.toFixed(2)])))
    if (conta.dados && !cliente) setCliente({ id: conta.dados.clienteId, nome: conta.dados.clienteNome, nuit: conta.dados.clienteNuit })
  }, [conta.dados]) // eslint-disable-line react-hooks/exhaustive-deps

  const activos = metodos.dados?.filter((m) => m.activo && metodoDeDinheiro(m)) ?? []
  const metodo = activos.find((m) => m.id === (metodoId ?? activos[0]?.id))
  const liquidacoes = (conta.dados?.facturas ?? []).map((f) => ({ factura: f, valor: numero(valores[f.id] ?? '') })).filter((x) => x.valor > 0)
  const total = liquidacoes.reduce((s, x) => s + x.valor, 0)
  const semCaixa = metodo?.contaNaGaveta && !sessao.dados

  const emitir = async () => {
    if (!clienteId || !metodo) return
    const excesso = liquidacoes.find((x) => x.valor > x.factura.emDivida)
    if (excesso) return setErro(`O valor da factura ${excesso.factura.numero} excede a dívida (${formatarMoeda(excesso.factura.emDivida)}).`)
    if (liquidacoes.length === 0) return setErro('Indique o valor a pagar de pelo menos uma factura.')
    if (metodo.exigeReferencia && !referencia.trim()) return setErro(`Indique a referência da transacção ${metodo.nome}.`)
    setErro(null)
    setAEmitir(true)
    try {
      const recibo = await api.post<DocumentoComercial>('/api/documentos/recibos', {
        clienteId, metodoPagamentoId: metodo.id, referencia: referencia.trim() || null, sessaoCaixaId: sessao.dados?.id ?? null, observacoes: null,
        liquidacoes: liquidacoes.map((x) => ({ facturaId: x.factura.id, valor: x.valor })),
      }, { 'Idempotency-Key': chave })
      aoEmitir(recibo.id)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAEmitir(false)
    }
  }

  return (
    <Modal titulo={titulo} aoFechar={aoFechar} largura="max-w-3xl"
      rodape={<>
        <span className="mr-auto self-center text-sm text-slate-600">Total a receber: <span className="font-semibold tabular-nums">{formatarMoeda(total)}</span></span>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aEmitir || !podeGravar || !metodo || semCaixa || liquidacoes.length === 0} onClick={() => void emitir()}>
          {aEmitir && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Emitir recibo
        </button>
      </>}>
      <div className="space-y-4 text-sm">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <SeletorCliente valor={cliente} aoMudar={(c) => { setCliente(c); setValores({}) }} />
        {clienteId && !conta.dados && !conta.erro && <Carregando />}
        {conta.erro && <Alerta tipo="erro">{conta.erro}</Alerta>}
        {conta.dados && conta.dados.facturas.length === 0 && <Alerta tipo="info">Este cliente não tem facturas em dívida.</Alerta>}
        {conta.dados && conta.dados.facturas.length > 0 && (
          <>
            <p className="text-slate-600">
              Em dívida: <span className="font-semibold">{formatarMoeda(conta.dados.totalEmDivida)}</span>
              {conta.dados.totalVencido > 0 && <span className="text-red-700"> · vencido: {formatarMoeda(conta.dados.totalVencido)}</span>}
            </p>
            <table className="w-full text-left">
              <thead className="bg-slate-50 text-slate-600">
                <tr><th scope="col" className="px-3 py-2">Factura</th><th scope="col" className="px-3 py-2">Vencimento</th><th scope="col" className="px-3 py-2 text-right">Em dívida</th><th scope="col" className="px-3 py-2 text-right">A pagar</th></tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {conta.dados.facturas.map((f) => (
                  <tr key={f.id}>
                    <td className="px-3 py-2 font-medium">{f.numero}</td>
                    <td className={`px-3 py-2 ${f.vencida ? 'font-medium text-red-700' : ''}`}>{f.vencimento ? dataCurta(f.vencimento) : '—'}{f.vencida && ' (vencida)'}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{formatarMoeda(f.emDivida)}</td>
                    <td className="px-3 py-2 text-right">
                      <input className="campo ml-auto w-32 text-right" inputMode="decimal" value={valores[f.id] ?? ''} placeholder="0,00" aria-label={`Valor a pagar da factura ${f.numero}`}
                        onChange={(e) => setValores({ ...valores, [f.id]: e.target.value })} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label htmlFor="rMet" className="rotulo">Forma de pagamento</label>
                <select id="rMet" className="campo" value={metodo?.id ?? ''} onChange={(e) => setMetodoId(Number(e.target.value))}>
                  {activos.map((m) => <option key={m.id} value={m.id}>{m.nome}</option>)}
                </select>
              </div>
              <div><label htmlFor="rRef" className="rotulo">Referência{metodo?.exigeReferencia ? ' (obrigatória)' : ''}</label><input id="rRef" className="campo" maxLength={100} value={referencia} onChange={(e) => setReferencia(e.target.value)} /></div>
            </div>
            {semCaixa
              ? <Alerta tipo="aviso">Para receber em {metodo?.nome} abra o caixa neste terminal: o valor entra na sessão de caixa.</Alerta>
              : sessao.dados && <p className="text-xs text-slate-500">Entra na sessão de caixa n.º {sessao.dados.id} deste terminal.</p>}
          </>
        )}
      </div>
    </Modal>
  )
}
