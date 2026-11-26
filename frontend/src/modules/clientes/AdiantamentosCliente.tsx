import { useState } from 'react'
import { CreditCard, Link2, Loader2, Plus, Printer, Wallet } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { DialogoImprimir } from '../../components/DialogoImprimir'
import { LeituraCartaoTelemovel } from '../../components/LeituraCartaoTelemovel'
import { Alerta, Carregando, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { CartaoEvento, Cliente, ContaCorrente, ExtratoAdiantamentos } from '../../types/api'
import { hojeIso, somarDias } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { Carregar, RegistarCartao } from '../cartoes/PaginaCartoes'
import { imprimirReciboCartao } from '../cartoes/reciboCartao'
import { NOME_MOVIMENTO_CARTAO } from '../cartoes/nomes'

/**
 * Adiantamentos do cliente: os Cartões Evento dele, o saldo por consumir, a posição global (adiantamentos menos a dívida em
 * facturas a crédito) e o extrato do período, com recarga, registo e associação de cartões.
 */
export function AdiantamentosCliente({ cliente }: { cliente: Pick<Cliente, 'id' | 'nome' | 'activo'> }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const podeGerir = tem('cartoes.gerir')
  const podeCliente = tem('cartoes.cliente')
  const verDivida = tem('facturacao.ver') || tem('pagamentos.registar')
  const [datas, setDatas] = useState({ desde: somarDias(hojeIso(), -30), ate: hojeIso() })
  const extrato = useDados(() => api.get<ExtratoAdiantamentos>(`/api/cartoes/clientes/${cliente.id}/extrato` + query(datas)), [cliente.id, datas.desde, datas.ate])
  const conta = useDados(() => (verDivida ? api.get<ContaCorrente>(`/api/documentos/conta-corrente/${cliente.id}`) : Promise.resolve(null)), [cliente.id, verDivida])
  const [recarregar, setRecarregar] = useState<{ id: number; numero: string } | null>(null)
  const [registar, setRegistar] = useState(false)
  const [associar, setAssociar] = useState(false)
  const [imprimir, setImprimir] = useState(false)
  const e = extrato.dados
  const divida = conta.dados?.totalEmDivida ?? 0
  const actualizar = () => void extrato.recarregar()

  return (
    <section aria-label="Adiantamentos" className="rounded-lg border border-slate-200 p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="flex items-center gap-1.5 font-semibold"><Wallet className="h-4 w-4 text-primaria-600" aria-hidden /> Adiantamentos (Cartões Evento)</h3>
          {e && (
            <p className="text-sm text-slate-600">
              Saldo por consumir <span className="font-semibold tabular-nums text-slate-900">{formatarMoeda(e.saldoActual)}</span> em {e.cartoes.length} cartão(ões)
              {verDivida && divida > 0 && (
                <> · em dívida {formatarMoeda(divida)} · posição global{' '}
                  <span className={`font-semibold tabular-nums ${e.saldoActual - divida < 0 ? 'text-red-700' : 'text-green-700'}`}>{formatarMoeda(e.saldoActual - divida)}</span></>
              )}
            </p>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          {podeGerir && podeCliente && cliente.activo && (
            <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setRegistar(true)}>
              <Plus className="h-4 w-4" aria-hidden /> Cartão novo
            </button>
          )}
          {podeCliente && cliente.activo && (
            <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setAssociar(true)}>
              <Link2 className="h-4 w-4" aria-hidden /> Associar cartão
            </button>
          )}
        </div>
      </div>

      {extrato.erro && <Alerta tipo="erro">{extrato.erro}</Alerta>}
      {!e && !extrato.erro && <Carregando />}
      {e && e.cartoes.length === 0 && (
        <p className="mt-2 text-sm text-slate-500">
          O cliente ainda não tem cartões. Registe um cartão novo para ele ou associe um cartão que ele já tenha; as recargas ficam como adiantamento e os consumos abatem.
        </p>
      )}
      {e && e.cartoes.length > 0 && (
        <ul className="mt-2 divide-y divide-slate-100 text-sm">
          {e.cartoes.map((c) => (
            <li key={c.id} className="flex flex-wrap items-center justify-between gap-2 py-1.5">
              <span className="flex items-center gap-2">
                <CreditCard className="h-4 w-4 text-slate-500" aria-hidden />
                <span className="font-medium tabular-nums">{c.numero}</span>
                {c.activo ? <Etiqueta cor="verde">Activo</Etiqueta> : <Etiqueta cor="cinza">Bloqueado</Etiqueta>}
                {c.ultimoMovimentoUtc && <span className="text-xs text-slate-500">últ. mov. {formatarDataHora(c.ultimoMovimentoUtc)}</span>}
              </span>
              <span className="flex items-center gap-2">
                <span className="font-semibold tabular-nums">{formatarMoeda(c.saldo)}</span>
                {podeGerir && c.activo && (
                  <button type="button" className="btn-primario min-h-9 px-3" disabled={!podeGravar} onClick={() => setRecarregar({ id: c.id, numero: c.numero })}>
                    <Wallet className="h-4 w-4" aria-hidden /> Recarregar
                  </button>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}

      {e && (e.cartoes.length > 0 || e.movimentos.length > 0) && (
        <div className="mt-3">
          <div className="mb-2 flex flex-wrap items-end gap-2">
            <h4 className="text-sm font-semibold">Extrato</h4>
            <label className="text-sm">De<input type="date" className="campo" value={datas.desde} max={datas.ate} onChange={(x) => setDatas((d) => ({ ...d, desde: x.target.value }))} /></label>
            <label className="text-sm">Até<input type="date" className="campo" value={datas.ate} min={datas.desde} onChange={(x) => setDatas((d) => ({ ...d, ate: x.target.value }))} /></label>
            <button type="button" className="btn-secundario" onClick={() => setImprimir(true)}><Printer className="h-4 w-4" aria-hidden /> Imprimir / PDF</button>
          </div>
          <p className="mb-1 text-sm text-slate-600">
            Saldo anterior {formatarMoeda(e.saldoAnterior)} · recargas <span className="text-green-700">{formatarMoeda(e.recargas)}</span>
            {' '}· consumos <span className="text-red-700">{formatarMoeda(e.consumos)}</span>
            {e.outros !== 0 && <> · outros {formatarMoeda(e.outros)}</>} · saldo final <strong>{formatarMoeda(e.saldoFinal)}</strong>
          </p>
          {e.movimentos.length === 0 ? <p className="text-sm text-slate-500">Sem movimentos nestas datas.</p> : (
            <div className="max-h-56 overflow-auto">
              <table className="w-full text-left text-sm">
                <thead className="sticky top-0 bg-superficie text-slate-600">
                  <tr className="border-b border-slate-200">
                    <th scope="col" className="py-1.5">Data/hora</th><th scope="col" className="py-1.5">Cartão</th><th scope="col" className="py-1.5">Movimento</th>
                    <th scope="col" className="py-1.5 text-right">Valor</th><th scope="col" className="py-1.5 text-right">Saldo</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 tabular-nums">
                  {[...e.movimentos].reverse().map((m) => (
                    <tr key={m.id}>
                      <td className="py-1">{formatarDataHora(m.dataUtc)}</td>
                      <td className="py-1">{m.cartao}</td>
                      <td className="py-1 text-slate-600">{[NOME_MOVIMENTO_CARTAO[m.tipo], m.metodo, m.pedido].filter(Boolean).join(' · ')}</td>
                      <td className={`py-1 text-right ${m.valor < 0 ? 'text-red-700' : 'text-green-700'}`}>{m.valor > 0 ? '+' : ''}{formatarMoeda(m.valor)}</td>
                      <td className="py-1 text-right">{formatarMoeda(m.saldo)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {e.cortado && <p className="mt-1 text-xs text-slate-500">Lista limitada: escolha um período mais curto.</p>}
        </div>
      )}

      {recarregar && <Carregar titulo={`Recarregar ${recarregar.numero} (${cliente.nome})`} url={`/api/cartoes/${recarregar.id}/recargas`} minimo={0.01}
        aoFechar={() => setRecarregar(null)}
        aoConcluir={(c) => { setRecarregar(null); avisos.sucesso(`Cartão recarregado. Saldo: ${formatarMoeda(c.saldo)}.`); actualizar(); void imprimirReciboCartao(c.id, avisos.erro) }} />}
      {registar && <RegistarCartao cliente={cliente} aoFechar={() => setRegistar(false)}
        aoAbrir={() => { setRegistar(false); setAssociar(true) }}
        aoRegistar={(c) => { setRegistar(false); actualizar(); if (c.saldo > 0) void imprimirReciboCartao(c.id, avisos.erro) }} />}
      {associar && <AssociarCartaoExistente cliente={cliente} aoFechar={() => setAssociar(false)} aoConcluir={() => { setAssociar(false); actualizar() }} />}
      {imprimir && <DialogoImprimir tipos={['ExtratoAdiantamentos']} desde={datas.desde} ate={datas.ate} clienteId={cliente.id} clienteNome={cliente.nome} aoFechar={() => setImprimir(false)} />}
    </section>
  )
}

/** Lê um cartão já registado (leitor, câmara ou NFC) e passa-o para este cliente. */
function AssociarCartaoExistente({ cliente, aoFechar, aoConcluir }: { cliente: Pick<Cliente, 'id' | 'nome'>; aoFechar: () => void; aoConcluir: () => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [lido, setLido] = useState('')
  const [cartao, setCartao] = useState<CartaoEvento | null>(null)
  const [aTrabalhar, setATrabalhar] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  const procurar = async (texto = lido) => {
    if (!texto.trim()) return
    setATrabalhar(true)
    setErro(null)
    try {
      setCartao(await api.get<CartaoEvento>('/api/cartoes/consulta' + query({ numero: texto })))
    } catch (x) {
      setCartao(null)
      setErro(mensagemErro(x))
    } finally {
      setATrabalhar(false)
    }
  }

  const associar = async () => {
    if (!cartao) return
    setATrabalhar(true)
    setErro(null)
    try {
      await api.post<CartaoEvento>(`/api/cartoes/${cartao.id}/cliente`, { clienteId: cliente.id, rowVersion: cartao.rowVersion })
      avisos.sucesso(`Cartão ${cartao.numero} associado a ${cliente.nome}.`)
      aoConcluir()
    } catch (x) {
      setErro(mensagemErro(x))
    } finally {
      setATrabalhar(false)
    }
  }

  const doCliente = cartao?.clienteId === cliente.id
  return (
    <Modal titulo={`Associar cartão a ${cliente.nome}`} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={!cartao || doCliente || aTrabalhar || !podeGravar} onClick={() => void associar()}>
          {aTrabalhar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Associar
        </button>
      </>}>
      <form className="space-y-3" onSubmit={(x) => { x.preventDefault(); void procurar() }}>
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div>
          <label htmlFor="cartaoAssociar" className="rotulo">Cartão</label>
          <div className="flex gap-2">
            <input id="cartaoAssociar" className="campo" autoFocus autoComplete="off" value={lido} placeholder="Passe o cartão, leia o código ou escreva o número"
              onChange={(x) => { setLido(x.target.value); setCartao(null) }} />
            <LeituraCartaoTelemovel desactivado={aTrabalhar} aoLer={(t) => { setLido(t); void procurar(t) }} />
          </div>
          <p className="mt-1 text-xs text-slate-500">Cartão ainda não registado? Use «Cartão novo».</p>
        </div>
        {cartao && (
          <Alerta tipo={doCliente ? 'info' : cartao.clienteId ? 'aviso' : 'sucesso'}>
            <strong className="tabular-nums">{cartao.numero}</strong> · saldo {formatarMoeda(cartao.saldo)}{!cartao.activo && ' · bloqueado'}
            <br />
            {doCliente ? 'Este cartão já é deste cliente.'
              : cartao.clienteId ? `É do cliente ${cartao.clienteNome}: ao associar, o saldo passa da conta dele para a de ${cliente.nome}.`
                : cartao.saldo > 0 ? `O saldo passa a contar como adiantamento de ${cliente.nome}.` : 'Cartão sem cliente.'}
          </Alerta>
        )}
      </form>
    </Modal>
  )
}
