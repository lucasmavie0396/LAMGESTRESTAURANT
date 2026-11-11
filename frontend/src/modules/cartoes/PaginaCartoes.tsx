import { useState } from 'react'
import { Barcode, CreditCard, Loader2, Lock, LockOpen, PackageOpen, Plus, Printer, QrCode, Search, Wallet } from 'lucide-react'
import { api, ErroApi, novaChaveIdempotencia, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { CartaoEvento, CodigoEtiquetaCartao, ExtratoCartao, MetodoPagamento, Pagina, SessaoCartoes } from '../../types/api'
import { hojeIso, somarDias } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { DialogoDescarga } from './DescargaCartoes'
import { NOME_MOVIMENTO_CARTAO } from './nomes'

/** Cartões Evento: consultar o saldo (passando o cartão), registar, recarregar, bloquear, extrato e etiquetas. */
export function PaginaCartoes() {
  const { tem } = useSessao()
  const avisos = useAvisos()
  const podeGerir = tem('cartoes.gerir')
  const [lido, setLido] = useState('')
  const [aConsultar, setAConsultar] = useState(false)
  const [escolhido, setEscolhido] = useState<CartaoEvento | null>(null)
  const [registar, setRegistar] = useState(false)
  const [descarregarTodos, setDescarregarTodos] = useState(false)
  const [pesquisa, setPesquisa] = useState('')
  const [pagina, setPagina] = useState(1)
  const atrasada = useAtraso(pesquisa)
  const lista = useDados(() => api.get<Pagina<CartaoEvento>>('/api/cartoes' + query({ pesquisa: atrasada, pagina, tamanho: 20 })), [atrasada, pagina])

  const consultar = async () => {
    if (!lido.trim()) return
    setAConsultar(true)
    try {
      setEscolhido(await api.get<CartaoEvento>('/api/cartoes/consulta' + query({ numero: lido })))
      setLido('')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAConsultar(false)
    }
  }

  const actualizado = (c: CartaoEvento) => {
    setEscolhido(c)
    void lista.recarregar()
  }

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Cartões Evento</h1>
        <div className="flex flex-wrap gap-2">
        {podeGerir && (
          <button type="button" className="btn-primario" onClick={() => setRegistar(true)}>
            <Plus className="h-4 w-4" aria-hidden /> Registar cartão
          </button>
        )}
        {podeGerir && (
          <button type="button" className="btn-secundario" onClick={() => setDescarregarTodos(true)}>
            <PackageOpen className="h-4 w-4" aria-hidden /> Descarregar todos os saldos
          </button>
        )}
        </div>
      </div>

      <section className="cartao p-4">
        <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); void consultar() }}>
          <div className="min-w-64 flex-1">
            <label htmlFor="cartaoLido" className="rotulo">Consultar saldo</label>
            <input id="cartaoLido" className="campo text-lg" autoFocus autoComplete="off" placeholder="Passe o cartão, leia o código ou escreva o número"
              value={lido} onChange={(e) => setLido(e.target.value)} />
          </div>
          <button type="submit" className="btn-primario" disabled={aConsultar || !lido.trim()}>
            {aConsultar ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Search className="h-4 w-4" aria-hidden />} Consultar
          </button>
        </form>
      </section>

      {escolhido && <DetalheCartao cartao={escolhido} podeGerir={podeGerir} aoActualizar={actualizado} aoFechar={() => setEscolhido(null)} />}

      <section className="cartao p-4">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-semibold">Cartões registados</h2>
          <input type="search" className="campo max-w-xs" placeholder="Número ou titular…" value={pesquisa} aria-label="Pesquisar cartões"
            onChange={(e) => { setPesquisa(e.target.value); setPagina(1) }} />
        </div>
        {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
        {!lista.dados && !lista.erro && <Carregando />}
        {lista.dados?.itens.length === 0 && <Vazio texto="Ainda não há cartões." />}
        {!!lista.dados?.itens.length && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Número</th>
                  <th scope="col" className="py-2">Titular</th>
                  <th scope="col" className="py-2 text-right">Saldo</th>
                  <th scope="col" className="py-2">Último movimento</th>
                  <th scope="col" className="py-2">Estado</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {lista.dados.itens.map((c) => (
                  <tr key={c.id} className="cursor-pointer hover:bg-slate-50" onClick={() => setEscolhido(c)}>
                    <td className="py-2 font-medium tabular-nums">
                      <button type="button" className="hover:underline" onClick={() => setEscolhido(c)}>{c.numero}</button>
                    </td>
                    <td className="py-2">{c.titular ?? '—'}</td>
                    <td className="py-2 text-right font-semibold tabular-nums">{formatarMoeda(c.saldo)}</td>
                    <td className="py-2 text-slate-600">{c.ultimoMovimentoUtc ? formatarDataHora(c.ultimoMovimentoUtc) : '—'}</td>
                    <td className="py-2">{c.activo ? <Etiqueta cor="verde">Activo</Etiqueta> : <Etiqueta cor="cinza">Bloqueado</Etiqueta>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {!!lista.dados?.total && <Paginacao pagina={pagina} tamanho={20} total={lista.dados.total} aoMudar={setPagina} />}
      </section>

      {descarregarTodos && <DialogoDescarga aoFechar={() => setDescarregarTodos(false)} aoConcluir={() => { setEscolhido(null); void lista.recarregar() }} />}
      {registar && <RegistarCartao aoFechar={() => setRegistar(false)} aoRegistar={(c) => { setRegistar(false); actualizado(c) }} />}
    </div>
  )
}

function DetalheCartao({ cartao, podeGerir, aoActualizar, aoFechar }: {
  cartao: CartaoEvento
  podeGerir: boolean
  aoActualizar: (c: CartaoEvento) => void
  aoFechar: () => void
}) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [pagina, setPagina] = useState(1)
  const [recarregar, setRecarregar] = useState(false)
  // Extrato: todo, por datas ou por caixa (sessão onde o dinheiro entrou ou onde se pagou).
  const [filtro, setFiltro] = useState<'tudo' | 'datas' | 'caixa'>('tudo')
  const [datas, setDatas] = useState({ desde: somarDias(hojeIso(), -30), ate: hojeIso() })
  const [sessao, setSessao] = useState<number | null>(null)
  const sessoes = useDados(() => (filtro === 'caixa' ? api.get<SessaoCartoes[]>(`/api/cartoes/${cartao.id}/sessoes`) : Promise.resolve(null)), [cartao, filtro])
  const sessaoEscolhida = sessao ?? sessoes.dados?.[0]?.id ?? null
  const parametrosFiltro = filtro === 'datas' ? { desde: datas.desde, ate: datas.ate } : filtro === 'caixa' ? { sessaoCaixaId: sessaoEscolhida } : {}
  const aguardaCaixa = filtro === 'caixa' && sessaoEscolhida === null
  const movimentos = useDados(
    () => (aguardaCaixa ? Promise.resolve(null) : api.get<ExtratoCartao>(`/api/cartoes/${cartao.id}/movimentos` + query({ ...parametrosFiltro, pagina, tamanho: 15 }))),
    [cartao, pagina, filtro, datas.desde, datas.ate, sessaoEscolhida])
  const mudarFiltro = (f: typeof filtro) => { setFiltro(f); setPagina(1) }

  const alternar = async () => {
    try {
      aoActualizar(await api.post<CartaoEvento>(`/api/cartoes/${cartao.id}/activo`, { activo: !cartao.activo, rowVersion: cartao.rowVersion }))
      avisos.sucesso(cartao.activo ? 'Cartão bloqueado.' : 'Cartão desbloqueado.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }
  const [etiqueta, setEtiqueta] = useState(false)
  const [descarregar, setDescarregar] = useState(false)

  return (
    <section className="cartao p-4" aria-label={`Cartão ${cartao.numero}`}>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <CreditCard className="h-10 w-10 text-primaria-600" aria-hidden />
          <div>
            <p className="text-lg font-semibold tabular-nums">{cartao.numero}</p>
            <p className="text-sm text-slate-600">{cartao.titular ?? 'Sem titular'} · {cartao.activo ? 'Activo' : 'Bloqueado'}</p>
          </div>
        </div>
        <div className="text-right">
          <p className="text-sm text-slate-600">Saldo</p>
          <p className="text-3xl font-bold tabular-nums text-primaria-700">{formatarMoeda(cartao.saldo)}</p>
        </div>
      </div>
      <div className="mt-3 flex flex-wrap gap-2">
        {podeGerir && cartao.activo && (
          <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setRecarregar(true)}>
            <Wallet className="h-4 w-4" aria-hidden /> Recarregar
          </button>
        )}
        {podeGerir && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setEtiqueta(true)}>
            <Printer className="h-4 w-4" aria-hidden /> Imprimir etiqueta
          </button>
        )}
        {podeGerir && cartao.saldo > 0 && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setDescarregar(true)}>
            <PackageOpen className="h-4 w-4" aria-hidden /> Descarregar saldo
          </button>
        )}
        {podeGerir && (
          <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => void alternar()}>
            {cartao.activo ? <Lock className="h-4 w-4" aria-hidden /> : <LockOpen className="h-4 w-4" aria-hidden />} {cartao.activo ? 'Bloquear' : 'Desbloquear'}
          </button>
        )}
        <button type="button" className="btn-secundario ml-auto" onClick={aoFechar}>Fechar</button>
      </div>

      <div className="mb-2 mt-4 flex flex-wrap items-end gap-3">
        <h3 className="text-sm font-semibold">Extrato</h3>
        <div role="radiogroup" aria-label="Filtro do extrato" className="flex gap-1 rounded-lg bg-slate-100 p-1">
          {([['tudo', 'Tudo'], ['datas', 'Por datas'], ['caixa', 'Por caixa']] as const).map(([v, t]) => (
            <button key={v} type="button" role="radio" aria-checked={filtro === v} onClick={() => mudarFiltro(v)}
              className={`min-h-9 rounded-md px-3 text-sm font-medium ${filtro === v ? 'bg-white text-primaria-700 shadow' : 'text-slate-600'}`}>{t}</button>
          ))}
        </div>
        {filtro === 'datas' && (
          <div className="flex items-end gap-2">
            <label className="text-sm">De<input type="date" className="campo" value={datas.desde} max={datas.ate}
              onChange={(e) => { setDatas((d) => ({ ...d, desde: e.target.value })); setPagina(1) }} /></label>
            <label className="text-sm">Até<input type="date" className="campo" value={datas.ate} min={datas.desde}
              onChange={(e) => { setDatas((d) => ({ ...d, ate: e.target.value })); setPagina(1) }} /></label>
          </div>
        )}
        {filtro === 'caixa' && (
          sessoes.dados?.length === 0 ? <p className="text-sm text-slate-500">O cartão ainda não teve movimentos em nenhum caixa.</p> : (
            <label className="min-w-64 text-sm">Caixa
              <select className="campo" value={sessaoEscolhida ?? ''} onChange={(e) => { setSessao(Number(e.target.value)); setPagina(1) }}>
                {sessoes.dados?.map((s) => (
                  <option key={s.id} value={s.id}>
                    N.º {s.id} · {s.terminal} · {s.abertaPor} · {formatarDataHora(s.abertaEmUtc)}{s.fechadaEmUtc ? '' : ' (aberto)'} · {s.movimentos} mov.
                  </option>
                ))}
              </select>
            </label>
          )
        )}
      </div>
      {movimentos.dados && filtro !== 'tudo' && (
        <p className="mb-2 text-sm text-slate-600">
          {movimentos.dados.total} movimentos · entradas <span className="font-medium text-green-700">{formatarMoeda(movimentos.dados.entradas)}</span>
          {' '}· saídas <span className="font-medium text-red-700">{formatarMoeda(movimentos.dados.saidas)}</span>
          {movimentos.dados.saldoInicial !== null && movimentos.dados.saldoFinal !== null && (
            <> · saldo no início {formatarMoeda(movimentos.dados.saldoInicial)} · no fim {formatarMoeda(movimentos.dados.saldoFinal)}</>
          )}
        </p>
      )}
      {movimentos.erro && <Alerta tipo="erro">{movimentos.erro}</Alerta>}
      {!movimentos.dados && !movimentos.erro && !aguardaCaixa && <Carregando />}
      {movimentos.dados && movimentos.dados.total === 0 && <p className="text-sm text-slate-500">Sem movimentos {filtro === 'datas' ? 'nestas datas' : filtro === 'caixa' ? 'neste caixa' : ''}.</p>}
      {movimentos.dados && movimentos.dados.total > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-slate-600">
              <tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Data/hora</th>
                <th scope="col" className="py-2">Movimento</th>
                <th scope="col" className="py-2">Detalhe</th>
                <th scope="col" className="py-2">Por</th>
                <th scope="col" className="py-2 text-right">Valor</th>
                <th scope="col" className="py-2 text-right">Saldo</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {movimentos.dados.itens.map((m) => (
                <tr key={m.id}>
                  <td className="py-1.5">{formatarDataHora(m.dataUtc)}</td>
                  <td className="py-1.5">{NOME_MOVIMENTO_CARTAO[m.tipo]}</td>
                  <td className="py-1.5 text-slate-600">{[m.metodo, m.pedido, m.motivo].filter(Boolean).join(' · ') || '—'}</td>
                  <td className="py-1.5 text-slate-600">{m.operador ?? '—'}</td>
                  <td className={`py-1.5 text-right ${m.valor < 0 ? 'text-red-700' : 'text-green-700'}`}>{m.valor > 0 ? '+' : ''}{formatarMoeda(m.valor)}</td>
                  <td className="py-1.5 text-right">{formatarMoeda(m.saldoApos)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {movimentos.dados.total > 15 && <Paginacao pagina={pagina} tamanho={15} total={movimentos.dados.total} aoMudar={setPagina} />}
        </div>
      )}
      {recarregar && <Carregar titulo={`Recarregar ${cartao.numero}`} url={`/api/cartoes/${cartao.id}/recargas`} minimo={0.01}
        aoFechar={() => setRecarregar(false)} aoConcluir={(c) => { setRecarregar(false); aoActualizar(c); avisos.sucesso(`Cartão recarregado. Saldo: ${formatarMoeda(c.saldo)}.`) }} />}
      {etiqueta && <ImprimirEtiqueta cartao={cartao} aoFechar={() => setEtiqueta(false)} />}
      {descarregar && <DialogoDescarga cartao={cartao} aoFechar={() => setDescarregar(false)}
        aoConcluir={() => { void api.get<CartaoEvento>(`/api/cartoes/${cartao.id}`).then(aoActualizar); void movimentos.recarregar() }} />}
    </section>
  )
}

const CODIGOS: { valor: CodigoEtiquetaCartao; rotulo: string; Icone: typeof Barcode; nota: string }[] = [
  { valor: 'CodigoBarras', rotulo: 'Código de barras', Icone: Barcode, nota: 'Lido por qualquer leitor de código de barras.' },
  { valor: 'QrCode', rotulo: 'QR code', Icone: QrCode, nota: 'Precisa de um leitor 2D (lê QR codes); o número sai por baixo.' },
]

/** Escolha do código da etiqueta do cartão (código de barras ou QR code), com pré-visualização, na impressora de etiquetas. */
function ImprimirEtiqueta({ cartao, aoFechar }: { cartao: CartaoEvento; aoFechar: () => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [codigo, setCodigo] = useState<CodigoEtiquetaCartao>('CodigoBarras')
  const [semPrevisao, setSemPrevisao] = useState(false)
  const [aImprimir, setAImprimir] = useState(false)

  const imprimir = async () => {
    setAImprimir(true)
    try {
      const r = await api.post<{ impressora: string }>('/api/cartoes/etiquetas', { cartoes: [cartao.id], codigo })
      avisos.sucesso(`Etiqueta do cartão (${CODIGOS.find((c) => c.valor === codigo)!.rotulo}) enviada para ${r.impressora}.`)
      aoFechar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAImprimir(false)
    }
  }

  return (
    <Modal titulo={`Etiqueta do cartão ${cartao.numero}`} aoFechar={aoFechar} largura="max-w-lg"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aImprimir || !podeGravar} onClick={() => void imprimir()}>
          {aImprimir ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Printer className="h-4 w-4" aria-hidden />} Imprimir
        </button>
      </>}>
      <fieldset className="space-y-2">
        <legend className="rotulo">Código a imprimir</legend>
        <div className="grid grid-cols-2 gap-2">
          {CODIGOS.map(({ valor, rotulo, Icone, nota }) => (
            <label key={valor} className={`flex cursor-pointer flex-col gap-1 rounded-lg border-2 p-3 text-sm ${codigo === valor ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
              <span className="flex items-center gap-2 font-medium">
                <input type="radio" name="codigoEtiqueta" className="h-4 w-4" checked={codigo === valor} onChange={() => { setCodigo(valor); setSemPrevisao(false) }} />
                <Icone className="h-5 w-5 text-slate-600" aria-hidden /> {rotulo}
              </span>
              <span className="text-xs text-slate-500">{nota}</span>
            </label>
          ))}
        </div>
      </fieldset>
      <div className="mt-4 flex min-h-32 items-center justify-center rounded-lg bg-slate-100 p-3">
        {semPrevisao
          ? <p className="text-sm text-slate-500">A pré-visualização só está disponível com o servidor em Windows.</p>
          : <img key={codigo} src={`/api/cartoes/${cartao.id}/etiqueta?codigo=${codigo}`} alt={`Pré-visualização da etiqueta com ${codigo === 'QrCode' ? 'QR code' : 'código de barras'}`}
              className="max-h-48 bg-white shadow ring-1 ring-slate-300" onError={() => setSemPrevisao(true)} />}
      </div>
      <p className="mt-2 text-xs text-slate-500">Sai na impressora de etiquetas, com o tamanho definido em Configurações → Etiquetas.</p>
    </Modal>
  )
}

/** Valor e método de pagamento: o dinheiro entra no caixa aberto de quem regista. */
function useMetodosCaixa() {
  return useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento').then((l) => l.filter((m) => m.activo && m.codigo !== 'CARTAO_EVENTO')), [])
}

function Carregar({ titulo, url, minimo, aoFechar, aoConcluir }: { titulo: string; url: string; minimo: number; aoFechar: () => void; aoConcluir: (c: CartaoEvento) => void }) {
  const metodos = useMetodosCaixa()
  const [valor, setValor] = useState('')
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [chave] = useState(novaChaveIdempotencia)
  const v = Number(valor.replace(',', '.'))
  const metodo = metodoId ?? metodos.dados?.[0]?.id ?? null
  const valido = v >= minimo && v <= 1_000_000 && metodo !== null

  const guardar = async () => {
    setAGuardar(true)
    setErro(null)
    try {
      aoConcluir(await api.post<CartaoEvento>(url, { valor: v, metodoPagamentoId: metodo }, { 'Idempotency-Key': chave }))
    } catch (e) {
      setErro(e instanceof ErroApi && e.semLigacao ? `${e.message} Pode tentar de novo sem risco de carregar duas vezes.` : mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={titulo} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={!valido || aGuardar} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Confirmar {valido ? formatarMoeda(v) : ''}
        </button>
      </>}>
      <div className="space-y-3">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div>
          <label htmlFor="valorCartao" className="rotulo">Valor (MT)</label>
          <input id="valorCartao" className="campo text-lg" inputMode="decimal" autoFocus value={valor} onChange={(e) => setValor(e.target.value.trim())} />
        </div>
        <ValorEMetodo metodos={metodos.dados ?? []} metodo={metodo} aoEscolher={setMetodoId} />
      </div>
    </Modal>
  )
}

function ValorEMetodo({ metodos, metodo, aoEscolher }: { metodos: MetodoPagamento[]; metodo: number | null; aoEscolher: (id: number) => void }) {
  return (
    <fieldset>
      <legend className="rotulo">Pago com (entra no seu caixa)</legend>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        {metodos.map((m) => (
          <button key={m.id} type="button" className={`${metodo === m.id ? 'btn-primario' : 'btn-secundario'} min-h-11`} aria-pressed={metodo === m.id} onClick={() => aoEscolher(m.id)}>
            {m.nome}
          </button>
        ))}
      </div>
    </fieldset>
  )
}

function RegistarCartao({ aoFechar, aoRegistar }: { aoFechar: () => void; aoRegistar: (c: CartaoEvento) => void }) {
  const avisos = useAvisos()
  const metodos = useMetodosCaixa()
  const [numero, setNumero] = useState('')
  const [titular, setTitular] = useState('')
  const [valor, setValor] = useState('')
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [chave] = useState(novaChaveIdempotencia)
  const v = valor.trim() === '' ? 0 : Number(valor.replace(',', '.'))
  const metodo = metodoId ?? metodos.dados?.[0]?.id ?? null
  const valido = Number.isFinite(v) && v >= 0 && v <= 1_000_000

  const guardar = async () => {
    setAGuardar(true)
    setErro(null)
    try {
      const c = await api.post<CartaoEvento>('/api/cartoes', { numero: numero.trim() || null, titular: titular.trim() || null, valorInicial: v, metodoPagamentoId: v > 0 ? metodo : null },
        { 'Idempotency-Key': chave })
      avisos.sucesso(`Cartão ${c.numero} registado com ${formatarMoeda(c.saldo)}.`)
      aoRegistar(c)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo="Registar Cartão Evento" aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={!valido || aGuardar} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Registar
        </button>
      </>}>
      <form className="space-y-3" onSubmit={(e) => e.preventDefault()}>
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div>
          <label htmlFor="numeroCartao" className="rotulo">Número do cartão</label>
          <input id="numeroCartao" className="campo" autoFocus autoComplete="off" value={numero} onChange={(e) => setNumero(e.target.value)}
            placeholder="Passe o cartão magnético, ou deixe vazio para gerar" />
          <p className="mt-1 text-xs text-slate-500">Vazio: o sistema gera um número de 12 algarismos para imprimir em código de barras e QR code.</p>
        </div>
        <div>
          <label htmlFor="titularCartao" className="rotulo">Titular (opcional)</label>
          <input id="titularCartao" className="campo" maxLength={150} value={titular} onChange={(e) => setTitular(e.target.value)} />
        </div>
        <div>
          <label htmlFor="valorInicial" className="rotulo">Valor inicial (MT)</label>
          <input id="valorInicial" className="campo text-lg" inputMode="decimal" value={valor} onChange={(e) => setValor(e.target.value.trim())} placeholder="0" />
        </div>
        {v > 0 && <ValorEMetodo metodos={metodos.dados ?? []} metodo={metodo} aoEscolher={setMetodoId} />}
      </form>
    </Modal>
  )
}
