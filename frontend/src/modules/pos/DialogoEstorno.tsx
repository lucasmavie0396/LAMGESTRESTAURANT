import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { MetodoPagamento, Pedido, SessaoCaixa } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { lerTerminalLocal } from '../../utils/terminalLocal'

/**
 * Estorno total de um pedido pago: devolve o valor pela sessão de caixa aberta deste terminal,
 * anula o recibo e repõe o stock. Exige permissão e motivo; fica na auditoria.
 */
export function DialogoEstorno({ pedido, aoEstornar, aoFechar }: { pedido: Pedido; aoEstornar: (p: Pedido) => void; aoFechar: () => void }) {
  const { podeGravar } = useLigacao()
  const terminalId = lerTerminalLocal()
  const sessao = useDados(
    () => (terminalId ? api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`) : Promise.resolve(undefined)),
    [terminalId],
  )
  const { dados: metodos } = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  // Por omissão, devolve pelo método do primeiro pagamento.
  const sugerido = metodos?.find((m) => m.nome === pedido.pagamentos.find((p) => p.estado === 'Valido')?.metodo)?.id ?? null
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const metodo = metodoId ?? sugerido
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aProcessar, setAProcessar] = useState(false)

  const estornar = async () => {
    if (!sessao.dados || !metodo) return
    setErro(null)
    setAProcessar(true)
    try {
      aoEstornar(await api.post<Pedido>(`/api/pedidos/${pedido.id}/estornar`, {
        sessaoCaixaId: sessao.dados.id, metodoPagamentoId: metodo, motivo: motivo.trim(), rowVersion: pedido.rowVersion,
      }))
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAProcessar(false)
    }
  }

  const semCaixa = !terminalId || (!sessao.aCarregar && !sessao.dados)
  return (
    <Modal titulo={`Estornar ${pedido.numero}`} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-perigo" onClick={() => void estornar()}
          disabled={aProcessar || !podeGravar || semCaixa || !metodo || motivo.trim().length < 3}>
          {aProcessar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Estornar {formatarMoeda(pedido.totalPago)}
        </button>
      </>}>
      {sessao.aCarregar ? <Carregando /> : semCaixa ? (
        <Alerta tipo="aviso">O reembolso sai da caixa: abra o caixa neste terminal. <Link to="/caixa" className="font-medium underline">Ir para o Caixa</Link></Alerta>
      ) : (
        <div className="space-y-4">
          {erro && <Alerta tipo="erro">{erro}</Alerta>}
          <p className="text-sm text-slate-600">
            O cliente recebe <strong>{formatarMoeda(pedido.totalPago)}</strong>. {pedido.reciboNumero?.startsWith('VD-') ? <>É emitida a nota de crédito da venda a dinheiro {pedido.reciboNumero}</> : <>O recibo {pedido.reciboNumero} fica anulado</>}, o stock é reposto e a operação
            fica registada. Estornos parciais não são suportados.
          </p>
          <div>
            <label htmlFor="metodoEstorno" className="rotulo">Devolver por</label>
            <select id="metodoEstorno" className="campo" value={metodo ?? ''} onChange={(e) => setMetodoId(Number(e.target.value))}>
              <option value="" disabled>Seleccione…</option>
              {metodos?.filter((m) => m.activo).map((m) => <option key={m.id} value={m.id}>{m.nome}</option>)}
            </select>
          </div>
          <div>
            <label htmlFor="motivoEstorno" className="rotulo">Motivo (obrigatório)</label>
            <textarea id="motivoEstorno" rows={3} className="campo py-2" maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </div>
          <p className="text-xs text-slate-500">Sessão de caixa n.º {sessao.dados?.id} · {sessao.dados?.terminal}</p>
        </div>
      )}
    </Modal>
  )
}
