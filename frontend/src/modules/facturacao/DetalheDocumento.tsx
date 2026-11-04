import { useState } from 'react'
import { Ban, FileCheck2, HandCoins, Loader2, Monitor, Printer, Undo2 } from 'lucide-react'
import { api, novaChaveIdempotencia } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Cliente, ConfiguracoesPublicas, DocumentoComercial, DocumentoImpressao, ImpressoraRelatorio } from '../../types/api'
import { dataCurta, hojeIso, somarDias } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { imprimirNoNavegador, PAPEIS, type LarguraPapel } from '../../utils/impressaoDocumento'
import { lerTerminalLocal } from '../../utils/terminalLocal'
import { DialogoMotivo } from '../pos/Dialogos'
import { SeletorCliente } from './FormDocumento'
import { NOMES_TIPO } from './nomes'
import { EtiquetaEstado } from './PaginaFacturacao'

const quantidade = (q: number) => q.toLocaleString('pt-MZ', { maximumFractionDigits: 3 })
const taxa = (p: number) => `${p.toLocaleString('pt-MZ', { maximumFractionDigits: 2 })} %`

export function DetalheDocumento({ id, aoFechar, aoAbrir, aoAlterar, aoReceber }: {
  id: number
  aoFechar: () => void
  aoAbrir: (id: number) => void
  aoAlterar: () => void
  aoReceber: (clienteId: number) => void
}) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const doc = useDados(() => api.get<DocumentoComercial>(`/api/documentos/${id}`), [id])
  const [dialogo, setDialogo] = useState<'imprimir' | 'anular' | 'converter' | 'credito' | null>(null)
  const d = doc.dados
  const emVigor = d?.estado === 'Emitido'

  const alterado = async (novoId?: number) => {
    setDialogo(null)
    aoAlterar()
    if (novoId && novoId !== id) aoAbrir(novoId)
    else await doc.recarregar()
  }

  return (
    <Modal titulo={d ? `${NOMES_TIPO[d.tipo]} ${d.numero}` : 'Documento'} aoFechar={aoFechar} largura="max-w-4xl"
      rodape={d && <>
        {emVigor && d.tipo === 'Cotacao' && tem('facturacao.anular') && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setDialogo('anular')}><Ban className="h-4 w-4" aria-hidden /> Anular</button>
        )}
        {emVigor && d.tipo === 'Recibo' && tem('facturacao.anular') && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setDialogo('anular')}><Ban className="h-4 w-4" aria-hidden /> Anular recibo</button>
        )}
        {emVigor && d.tipo === 'Factura' && d.emDivida > 0 && tem('facturacao.anular') && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setDialogo('credito')}><Undo2 className="h-4 w-4" aria-hidden /> Nota de crédito</button>
        )}
        {emVigor && d.tipo === 'Cotacao' && tem('facturacao.emitir') && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setDialogo('converter')}><FileCheck2 className="h-4 w-4" aria-hidden /> Converter em factura</button>
        )}
        {emVigor && d.tipo === 'Factura' && d.emDivida > 0 && d.clienteId && tem('facturacao.emitir') && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => aoReceber(d.clienteId!)}><HandCoins className="h-4 w-4" aria-hidden /> Receber</button>
        )}
        <button type="button" className="btn-primario" onClick={() => setDialogo('imprimir')}><Printer className="h-4 w-4" aria-hidden /> Imprimir</button>
      </>}>
      {doc.erro && <Alerta tipo="erro">{doc.erro}</Alerta>}
      {!d && !doc.erro && <Carregando />}
      {d && (
        <div className="space-y-4 text-sm">
          {d.estado === 'Anulado' && (
            <Alerta tipo="erro" titulo="Documento anulado">
              {d.anuladoEmUtc && formatarDataHora(d.anuladoEmUtc)}{d.anuladoPor && ` por ${d.anuladoPor}`}: {d.motivoAnulacao}
            </Alerta>
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <p className="text-xs uppercase text-slate-500">Cliente</p>
              <p className="font-medium">{d.clienteNome}</p>
              <p className="text-slate-600">NUIT {d.clienteNuit ?? '—'}</p>
              {d.clienteEndereco && <p className="text-slate-600">{d.clienteEndereco}</p>}
            </div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
              <dt className="text-slate-500">Estado</dt><dd><EtiquetaEstado tipo={d.tipo} estado={d.estado} emDivida={d.emDivida} /></dd>
              <dt className="text-slate-500">Data</dt><dd>{formatarDataHora(d.emitidoEmUtc)}{d.utilizador && ` · ${d.utilizador}`}</dd>
              {d.vencimento && <><dt className="text-slate-500">Vencimento</dt><dd>{dataCurta(d.vencimento)}</dd></>}
              {d.validaAte && <><dt className="text-slate-500">Válida até</dt><dd>{dataCurta(d.validaAte)}</dd></>}
              {d.pedidoNumero && <><dt className="text-slate-500">Pedido</dt><dd>{d.pedidoNumero}</dd></>}
              {d.documentoOrigemId && d.documentoOrigemTipo && (
                <><dt className="text-slate-500">{d.tipo === 'Factura' ? 'Cotação' : 'Referente a'}</dt>
                  <dd><button type="button" className="font-medium text-primaria-700 hover:underline" onClick={() => aoAbrir(d.documentoOrigemId!)}>{NOMES_TIPO[d.documentoOrigemTipo]} {d.documentoOrigemNumero}</button></dd></>
              )}
              {d.motivo && <><dt className="text-slate-500">Motivo</dt><dd>{d.motivo}</dd></>}
              {d.metodoPagamento && <><dt className="text-slate-500">Pagamento</dt><dd>{d.metodoPagamento}{d.referencia && ` (${d.referencia})`}</dd></>}
            </dl>
          </div>

          {d.tipo === 'Recibo' ? (
            <table className="w-full text-left">
              <thead className="bg-slate-50 text-slate-600"><tr><th scope="col" className="px-3 py-2">Factura liquidada</th><th scope="col" className="px-3 py-2 text-right">Valor</th></tr></thead>
              <tbody className="divide-y divide-slate-100">
                {d.liquidacoes.map((l) => (
                  <tr key={l.facturaId}>
                    <td className="px-3 py-2"><button type="button" className="font-medium text-primaria-700 hover:underline" onClick={() => aoAbrir(l.facturaId)}>{l.facturaNumero}</button></td>
                    <td className="px-3 py-2 text-right tabular-nums">{formatarMoeda(l.valor)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left">
                <thead className="bg-slate-50 text-slate-600">
                  <tr>
                    <th scope="col" className="px-3 py-2">Descrição</th>
                    <th scope="col" className="px-3 py-2 text-right">Qtd.</th>
                    <th scope="col" className="px-3 py-2 text-right">Preço</th>
                    <th scope="col" className="px-3 py-2 text-right">Desconto</th>
                    <th scope="col" className="px-3 py-2 text-right">IVA</th>
                    <th scope="col" className="px-3 py-2 text-right">Total</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {d.linhas.map((l) => (
                    <tr key={l.id}>
                      <td className="px-3 py-2">{l.descricao}{l.codigo && <span className="text-xs text-slate-500"> · {l.codigo}</span>}
                        {l.motivoIsencao && <p className="text-xs text-slate-500">{l.motivoIsencao}</p>}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{quantidade(l.quantidade)}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{formatarMoeda(l.precoUnitario)}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{l.desconto > 0 ? formatarMoeda(l.desconto) : '—'}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{taxa(l.percentagemIva)}</td>
                      <td className="px-3 py-2 text-right font-medium tabular-nums">{formatarMoeda(l.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              {d.resumoIva.length > 0 && (
                <table className="w-full text-left text-xs">
                  <thead className="text-slate-500"><tr><th scope="col" className="py-1">Taxa</th><th scope="col" className="py-1 text-right">Base</th><th scope="col" className="py-1 text-right">IVA</th></tr></thead>
                  <tbody>{d.resumoIva.map((r) => (
                    <tr key={r.percentagem}><td className="py-1">IVA {taxa(r.percentagem)}</td><td className="py-1 text-right tabular-nums">{formatarMoeda(r.base)}</td><td className="py-1 text-right tabular-nums">{formatarMoeda(r.iva)}</td></tr>
                  ))}</tbody>
                </table>
              )}
              {d.observacoes && <p className="mt-2 text-slate-600">{d.observacoes}</p>}
            </div>
            <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1 text-right">
              {d.tipo !== 'Recibo' && <>
                <dt className="text-slate-500">Subtotal</dt><dd className="tabular-nums">{formatarMoeda(d.subtotal)}</dd>
                {d.desconto > 0 && <><dt className="text-slate-500">Descontos</dt><dd className="tabular-nums">−{formatarMoeda(d.desconto)}</dd></>}
                <dt className="text-slate-500">Total sem IVA</dt><dd className="tabular-nums">{formatarMoeda(d.baseTributavel)}</dd>
                <dt className="text-slate-500">IVA</dt><dd className="tabular-nums">{formatarMoeda(d.iva)}</dd>
              </>}
              <dt className="text-base font-semibold">{d.tipo === 'NotaCredito' ? 'Total creditado' : d.tipo === 'Recibo' ? 'Total recebido' : 'Total'}</dt>
              <dd className="text-base font-semibold tabular-nums">{formatarMoeda(d.total)}</dd>
              {d.valorPago > 0 && <><dt className="text-slate-500">Pago</dt><dd className="tabular-nums">{formatarMoeda(d.valorPago)}</dd></>}
              {d.valorCreditado > 0 && <><dt className="text-slate-500">Creditado</dt><dd className="tabular-nums">{formatarMoeda(d.valorCreditado)}</dd></>}
              {d.tipo === 'Factura' && emVigor && <><dt className="font-medium">Em dívida</dt><dd className="font-medium tabular-nums">{formatarMoeda(d.emDivida)}</dd></>}
            </dl>
          </div>

          {d.relacionados.length > 0 && (
            <div>
              <p className="mb-1 text-xs uppercase text-slate-500">Documentos relacionados</p>
              <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
                {d.relacionados.map((r) => (
                  <li key={`${r.tipo}-${r.id}`} className="flex items-center justify-between px-3 py-2">
                    <button type="button" className={`font-medium text-primaria-700 hover:underline ${r.estado === 'Anulado' ? 'line-through' : ''}`} onClick={() => aoAbrir(r.id)}>
                      {NOMES_TIPO[r.tipo]} {r.numero}
                    </button>
                    <span className="text-slate-600">{formatarDataHora(r.emitidoEmUtc)} · <span className="tabular-nums">{formatarMoeda(r.valor)}</span>{r.estado === 'Anulado' && ' · anulado'}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {d && dialogo === 'imprimir' && <DialogoImprimirDocumento documento={d} aoFechar={() => setDialogo(null)} />}
      {d && dialogo === 'anular' && (
        <DialogoMotivo titulo={`Anular ${NOMES_TIPO[d.tipo].toLowerCase()} ${d.numero}`} texto="Anular"
          descricao={d.tipo === 'Recibo'
            ? 'O valor volta à dívida das facturas. Se a sessão de caixa do recibo ainda estiver aberta, o valor sai da caixa; se já fechou, o acerto do dinheiro é manual.'
            : 'A cotação deixa de poder ser convertida. Fica registada na auditoria.'}
          aoFechar={() => setDialogo(null)}
          aoConfirmar={async (motivo) => {
            try {
              await api.post(`/api/documentos/${d.id}/anular`, { motivo, rowVersion: d.rowVersion })
              avisos.sucesso(`${NOMES_TIPO[d.tipo]} anulado.`)
              await alterado()
            } catch (e) {
              avisos.erro(mensagemErro(e))
            }
          }} />
      )}
      {d && dialogo === 'converter' && <DialogoConverter cotacao={d} aoFechar={() => setDialogo(null)} aoConverter={(novo) => void alterado(novo)} />}
      {d && dialogo === 'credito' && <DialogoNotaCredito factura={d} aoFechar={() => setDialogo(null)} aoEmitir={(novo) => void alterado(novo)} />}
    </Modal>
  )
}

/** Imprimir pelo navegador (escolhe-se o papel; A4 por omissão para facturas) ou numa impressora do servidor. */
function DialogoImprimirDocumento({ documento, aoFechar }: { documento: DocumentoComercial; aoFechar: () => void }) {
  const avisos = useAvisos()
  const impressoras = useDados(() => api.get<ImpressoraRelatorio[]>('/api/relatorios/impressoras'), [])
  const publicas = useDados(() => api.get<ConfiguracoesPublicas>('/api/configuracoes/publicas'), [])
  const [papel, setPapel] = useState<LarguraPapel>(documento.tipo === 'VendaDinheiro' ? 80 : 210)
  const [destino, setDestino] = useState<'navegador' | number>('navegador')
  const [aImprimir, setAImprimir] = useState(false)

  const imprimir = async () => {
    setAImprimir(true)
    try {
      if (destino === 'navegador') {
        imprimirNoNavegador(await api.get<DocumentoImpressao>(`/api/documentos/${documento.id}/impressao`), papel, publicas.dados?.logotipo ?? null)
      } else {
        const r = await api.post<{ impressora: string; pdf: boolean }>(`/api/documentos/${documento.id}/imprimir`, { impressoraId: destino, terminalId: lerTerminalLocal() })
        avisos.sucesso(r.pdf ? `${documento.numero}: o PDF abre dentro de momentos.` : `${documento.numero} enviado para a impressora ${r.impressora}.`)
      }
      aoFechar()
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível imprimir.'))
    } finally {
      setAImprimir(false)
    }
  }

  return (
    <Modal titulo={`Imprimir ${documento.numero}`} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aImprimir} onClick={() => void imprimir()}>
          {aImprimir ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Printer className="h-4 w-4" aria-hidden />} Imprimir
        </button>
      </>}>
      <fieldset className="space-y-2">
        <legend className="rotulo">Onde</legend>
        <label className="flex min-h-11 items-center gap-2 rounded-lg border border-slate-200 px-3">
          <input type="radio" name="destino" checked={destino === 'navegador'} onChange={() => setDestino('navegador')} />
          <Monitor className="h-4 w-4 text-slate-500" aria-hidden /> Este computador (janela de impressão; também PDF)
        </label>
        {destino === 'navegador' && (
          <div className="flex gap-2 pl-8">
            {PAPEIS.map((p) => (
              <label key={p.valor} className="flex items-center gap-1 text-sm">
                <input type="radio" name="papel" checked={papel === p.valor} onChange={() => setPapel(p.valor)} /> {p.rotulo}
              </label>
            ))}
          </div>
        )}
        {impressoras.dados?.map((i) => (
          <label key={i.id} className="flex min-h-11 items-center gap-2 rounded-lg border border-slate-200 px-3">
            <input type="radio" name="destino" checked={destino === i.id} onChange={() => setDestino(i.id)} />
            <Printer className="h-4 w-4 text-slate-500" aria-hidden /> {i.nome}
            <span className="text-xs text-slate-500">({i.larguraPapelMm === 210 ? 'A4' : `${i.larguraPapelMm} mm`}{i.modo === 'Pdf' ? ', PDF' : ''})</span>
          </label>
        ))}
      </fieldset>
    </Modal>
  )
}

function DialogoConverter({ cotacao, aoFechar, aoConverter }: { cotacao: DocumentoComercial; aoFechar: () => void; aoConverter: (id: number) => void }) {
  const avisos = useAvisos()
  const [cliente, setCliente] = useState<Pick<Cliente, 'id' | 'nome' | 'nuit'> | null>(
    cotacao.clienteId ? { id: cotacao.clienteId, nome: cotacao.clienteNome, nuit: cotacao.clienteNuit } : null)
  const [vencimento, setVencimento] = useState(() => somarDias(hojeIso(), 30))
  const [chave] = useState(novaChaveIdempotencia)
  const [aProcessar, setAProcessar] = useState(false)

  const converter = async () => {
    if (!cliente) return
    setAProcessar(true)
    try {
      const factura = await api.post<DocumentoComercial>(`/api/documentos/cotacoes/${cotacao.id}/converter`,
        { clienteId: cliente.id, vencimento, rowVersion: cotacao.rowVersion }, { 'Idempotency-Key': chave })
      avisos.sucesso(`Factura ${factura.numero} emitida.`)
      aoConverter(factura.id)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAProcessar(false)
    }
  }

  return (
    <Modal titulo={`Converter ${cotacao.numero} em factura`} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={!cliente || aProcessar} onClick={() => void converter()}>
          {aProcessar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Emitir factura de {formatarMoeda(cotacao.total)}
        </button>
      </>}>
      <div className="space-y-4">
        <p className="text-sm text-slate-600">A factura fica com as mesmas linhas e preços da cotação, a crédito do cliente escolhido.</p>
        <SeletorCliente valor={cliente} aoMudar={setCliente} />
        <div><label htmlFor="cVenc" className="rotulo">Vencimento</label><input id="cVenc" type="date" className="campo w-48" min={hojeIso()} value={vencimento} onChange={(e) => setVencimento(e.target.value)} /></div>
      </div>
    </Modal>
  )
}

/** Nota de crédito sobre uma factura: por quantidades de cada linha ou tudo o que ainda não foi creditado. */
function DialogoNotaCredito({ factura, aoFechar, aoEmitir }: { factura: DocumentoComercial; aoFechar: () => void; aoEmitir: (id: number) => void }) {
  const avisos = useAvisos()
  const [motivo, setMotivo] = useState('')
  const [quantidades, setQuantidades] = useState<Record<number, string>>({})
  const [chave] = useState(novaChaveIdempotencia)
  const [aProcessar, setAProcessar] = useState(false)
  const creditaveis = factura.linhas.filter((l) => l.quantidadeCreditavel > 0)
  const escolhidas = creditaveis.map((l) => ({ linha: l, qtd: Number((quantidades[l.id] ?? '').replace(',', '.')) })).filter((x) => x.qtd > 0)
  // Pré-visualização: o valor final é calculado pelo servidor.
  const previsto = escolhidas.reduce((s, x) => s + (x.linha.total / x.linha.quantidade) * x.qtd, 0)

  const emitir = async (tudo: boolean) => {
    if (motivo.trim().length < 3) return avisos.erro('Indique o motivo (pelo menos 3 caracteres).')
    setAProcessar(true)
    try {
      const nota = await api.post<DocumentoComercial>('/api/documentos/notas-credito', {
        documentoOrigemId: factura.id, motivo: motivo.trim(),
        linhas: tudo ? null : escolhidas.map((x) => ({ linhaOrigemId: x.linha.id, quantidade: x.qtd })),
      }, { 'Idempotency-Key': chave })
      avisos.sucesso(`Nota de crédito ${nota.numero} emitida (${formatarMoeda(nota.total)}).`)
      aoEmitir(nota.id)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAProcessar(false)
    }
  }

  return (
    <Modal titulo={`Nota de crédito da factura ${factura.numero}`} aoFechar={aoFechar} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-secundario" disabled={aProcessar} onClick={() => void emitir(true)}>Creditar tudo</button>
        <button type="button" className="btn-primario" disabled={aProcessar || escolhidas.length === 0} onClick={() => void emitir(false)}>
          {aProcessar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Creditar ≈ {formatarMoeda(previsto)}
        </button>
      </>}>
      <div className="space-y-4 text-sm">
        <Alerta tipo="info">Só se credita o que ainda está em dívida ({formatarMoeda(factura.emDivida)}). Para creditar um valor já pago, anule primeiro o recibo.</Alerta>
        <div><label htmlFor="ncMotivo" className="rotulo">Motivo</label><input id="ncMotivo" className="campo" maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Ex.: devolução de 2 caixas" /></div>
        <table className="w-full text-left">
          <thead className="bg-slate-50 text-slate-600"><tr><th scope="col" className="px-3 py-2">Linha</th><th scope="col" className="px-3 py-2 text-right">Pode creditar</th><th scope="col" className="px-3 py-2 text-right">Quantidade a creditar</th></tr></thead>
          <tbody className="divide-y divide-slate-100">
            {creditaveis.map((l) => (
              <tr key={l.id}>
                <td className="px-3 py-2">{l.descricao}</td>
                <td className="px-3 py-2 text-right tabular-nums">{quantidade(l.quantidadeCreditavel)}</td>
                <td className="px-3 py-2 text-right">
                  <input className="campo ml-auto w-28 text-right" inputMode="decimal" value={quantidades[l.id] ?? ''} aria-label={`Quantidade a creditar de ${l.descricao}`}
                    onChange={(e) => setQuantidades({ ...quantidades, [l.id]: e.target.value })} placeholder="0" />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Modal>
  )
}
