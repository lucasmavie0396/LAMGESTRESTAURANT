import { useState } from 'react'
import { Loader2, PackageOpen } from 'lucide-react'
import { api, ErroApi, novaChaveIdempotencia, query } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { CartaoEvento, DescargaCartoes, MetodoPagamento, SaldosDescarga } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { metodoDeDinheiro } from '../../utils/metodos'

/** Tabela de saldos com o acumulado linha a linha. */
function TabelaSaldos({ linhas, total }: { linhas: { numero: string; titular: string | null; saldo: number; acumulado: number }[]; total: number }) {
  return (
    <div className="max-h-72 overflow-auto rounded-lg ring-1 ring-slate-200">
      <table className="w-full text-left text-sm">
        <thead className="sticky top-0 bg-slate-50 text-slate-600"><tr>
          <th scope="col" className="px-3 py-2">Cartão</th><th scope="col" className="px-3 py-2">Titular</th>
          <th scope="col" className="px-3 py-2 text-right">Saldo</th><th scope="col" className="px-3 py-2 text-right">Acumulado</th>
        </tr></thead>
        <tbody className="divide-y divide-slate-100 tabular-nums">
          {linhas.map((l) => (
            <tr key={l.numero}>
              <td className="px-3 py-1.5">{l.numero}</td><td className="px-3 py-1.5 text-slate-600">{l.titular ?? '—'}</td>
              <td className="px-3 py-1.5 text-right">{formatarMoeda(l.saldo)}</td><td className="px-3 py-1.5 text-right">{formatarMoeda(l.acumulado)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot className="sticky bottom-0 bg-slate-50 font-semibold"><tr>
          <td className="px-3 py-2" colSpan={3}>Total ({linhas.length} {linhas.length === 1 ? 'cartão' : 'cartões'})</td>
          <td className="px-3 py-2 text-right tabular-nums">{formatarMoeda(total)}</td>
        </tr></tfoot>
      </table>
    </div>
  )
}

/** Descarga do saldo restante: de um cartão ou de todos os que têm saldo. Mostra o saldo de cada um e o acumulado antes de confirmar. */
export function DialogoDescarga({ cartao, aoFechar, aoConcluir }: { cartao?: CartaoEvento; aoFechar: () => void; aoConcluir: () => void }) {
  const { podeGravar } = useLigacao()
  const saldos = useDados(() => api.get<SaldosDescarga>('/api/cartoes/descargas/saldos' + (cartao ? query({ id: cartao.id }) : '')), [cartao?.id])
  const metodos = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento').then((l) => l.filter((m) => m.activo && metodoDeDinheiro(m))), [])
  const [devolver, setDevolver] = useState(true)
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [referencia, setReferencia] = useState('')
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aGravar, setAGravar] = useState(false)
  const [feita, setFeita] = useState<DescargaCartoes | null>(null)
  const [chave] = useState(novaChaveIdempotencia)

  const metodo = metodos.dados?.find((m) => m.id === metodoId) ?? metodos.dados?.[0] ?? null
  const dados = saldos.dados
  const valido = !!dados && dados.cartoes.length > 0 && motivo.trim().length >= 3 && (!devolver || (metodo !== null && (!metodo.exigeReferencia || referencia.trim().length > 0)))

  const confirmar = async () => {
    if (!dados) return
    if (!window.confirm(`Descarregar ${formatarMoeda(dados.total)} de ${dados.cartoes.length} cartão(ões)? O saldo fica a zero e a operação não se pode desfazer.`)) return
    setAGravar(true)
    setErro(null)
    try {
      setFeita(await api.post<DescargaCartoes>('/api/cartoes/descargas', {
        cartoes: dados.cartoes.map((c) => ({ id: c.id, saldo: c.saldo })),
        devolverDinheiro: devolver, metodoPagamentoId: devolver ? metodo?.id ?? null : null, referencia: devolver ? referencia.trim() || null : null, motivo: motivo.trim(),
      }, { 'Idempotency-Key': chave }))
      aoConcluir()
    } catch (e) {
      setErro(e instanceof ErroApi && e.semLigacao ? `${e.message} Pode tentar de novo sem risco de descarregar duas vezes.` : mensagemErro(e))
      if (e instanceof ErroApi && e.estado === 409) void saldos.recarregar()
    } finally {
      setAGravar(false)
    }
  }

  if (feita)
    return (
      <Modal titulo="Descarga concluída" aoFechar={aoFechar} largura="max-w-2xl" rodape={<button type="button" className="btn-primario" onClick={aoFechar}>Fechar</button>}>
        <div className="space-y-3">
          <Alerta tipo="sucesso">
            {formatarMoeda(feita.total)} descarregados em {formatarDataHora(feita.dataUtc)}.{' '}
            {feita.devolveuDinheiro ? `Dinheiro devolvido por ${feita.metodo}, saído do caixa n.º ${feita.sessaoCaixaId}.` : 'Saldo retirado, sem devolução de dinheiro.'}
          </Alerta>
          <TabelaSaldos linhas={feita.cartoes} total={feita.total} />
        </div>
      </Modal>
    )

  return (
    <Modal titulo={cartao ? `Descarregar o cartão ${cartao.numero}` : 'Descarregar todos os cartões'} aoFechar={aoFechar} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={!valido || aGravar || !podeGravar} onClick={() => void confirmar()}>
          {aGravar ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <PackageOpen className="h-4 w-4" aria-hidden />} Descarregar {dados ? formatarMoeda(dados.total) : ''}
        </button>
      </>}>
      <div className="space-y-4">
        {saldos.erro && <Alerta tipo="erro">{saldos.erro}</Alerta>}
        {!dados && !saldos.erro && <Carregando />}
        {dados?.cartoes.length === 0 && <Alerta tipo="info">{cartao ? 'Este cartão não tem saldo.' : 'Nenhum cartão tem saldo por descarregar.'}</Alerta>}
        {dados && dados.cartoes.length > 0 && (
          <>
            <TabelaSaldos linhas={dados.cartoes} total={dados.total} />
            <fieldset className="space-y-2">
              <legend className="rotulo">O que acontece ao dinheiro</legend>
              <label className="flex items-center gap-2 text-sm"><input type="radio" name="devolver" className="h-4 w-4" checked={devolver} onChange={() => setDevolver(true)} /> Devolver ao cliente (sai do meu caixa)</label>
              <label className="flex items-center gap-2 text-sm"><input type="radio" name="devolver" className="h-4 w-4" checked={!devolver} onChange={() => setDevolver(false)} /> Só retirar o saldo (não sai dinheiro)</label>
            </fieldset>
            {devolver && (
              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <label htmlFor="descargaMetodo" className="rotulo">Devolvido por</label>
                  <select id="descargaMetodo" className="campo" value={metodo?.id ?? ''} onChange={(e) => setMetodoId(Number(e.target.value))}>
                    {metodos.dados?.map((m) => <option key={m.id} value={m.id}>{m.nome}</option>)}
                  </select>
                </div>
                {metodo?.exigeReferencia && (
                  <div><label htmlFor="descargaRef" className="rotulo">Referência</label><input id="descargaRef" className="campo" maxLength={100} value={referencia} onChange={(e) => setReferencia(e.target.value)} /></div>
                )}
              </div>
            )}
            <div><label htmlFor="descargaMotivo" className="rotulo">Motivo</label><input id="descargaMotivo" className="campo" maxLength={250} placeholder="Ex.: fim do evento" value={motivo} onChange={(e) => setMotivo(e.target.value)} /></div>
          </>
        )}
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
      </div>
    </Modal>
  )
}
