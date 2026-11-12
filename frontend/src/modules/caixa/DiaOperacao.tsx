import { useState } from 'react'
import { CalendarCheck, CalendarX, Loader2, Printer } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { DialogoImprimir } from '../../components/DialogoImprimir'
import { Alerta, Carregando, Confirmacao, Etiqueta, Modal, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { DiaOperacao, EstadoDiaOperacao, Pagina, ResumoDia, TipoMovimentoCaixa } from '../../types/api'
import { dataCurta, horaLocal } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { P } from '../../utils/permissoes'
import { imprimirFechoAutomatico } from '../../utils/fechoAutomatico'

const NOMES_MOVIMENTO: Record<TipoMovimentoCaixa, string> = {
  Entrada: 'Entradas (recargas, recebimentos, reforços)',
  Saida: 'Saídas',
  Despesa: 'Despesas pagas pelo caixa',
  Reembolso: 'Reembolsos (estornos)',
}

const pct = (parte: number, total: number) => (total === 0 ? '—' : `${((parte * 100) / total).toFixed(1).replace('.', ',')}`)
const comSinal = (v: number) => (v > 0 ? '+' : '') + formatarMoeda(v)
const corDiferenca = (v: number | null) => (v ? (v > 0 ? 'text-green-700' : 'text-red-700') : '')

/** Fecho do dia (ou ponto de situação) em imprimir: talão 80/58 mm ou A4. */
export function BotaoImprimirDia({ diaId, texto = 'Imprimir' }: { diaId: number; texto?: string }) {
  const [aberto, setAberto] = useState(false)
  return (
    <>
      <button type="button" className="btn-secundario" onClick={() => setAberto(true)}>
        <Printer className="h-4 w-4" aria-hidden /> {texto}
      </button>
      {aberto && <DialogoImprimir tipos={['FechoDia']} diaId={diaId} aoFechar={() => setAberto(false)} />}
    </>
  )
}

/**
 * Barra do dia de operação no topo do menu Caixa: estado do dia e botões «Abrir Dia» / «Fechar Dia» (permissão caixa.dia).
 * `aoMudar` recarrega o estado (o caixa deste terminal depende do dia aberto).
 */
export function PainelDia({ estado, aoMudar }: { estado: EstadoDiaOperacao; aoMudar: () => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const [dialogo, setDialogo] = useState<'abrir' | 'fechar' | null>(null)
  const [fechado, setFechado] = useState<ResumoDia | null>(null)
  const gere = tem(P.caixaDia)
  const dia = estado.aberto

  return (
    <div className={`cartao flex flex-wrap items-center gap-3 p-4 print:hidden ${dia ? 'border-l-4 border-l-green-600' : 'border-l-4 border-l-amber-500'}`}>
      {dia ? <CalendarCheck className="h-6 w-6 text-green-700" aria-hidden /> : <CalendarX className="h-6 w-6 text-amber-600" aria-hidden />}
      <div className="min-w-0 flex-1">
        <p className="font-semibold" data-testid="estado-dia">
          {dia ? `Dia ${dataCurta(dia.data)} aberto` : 'Dia não aberto'}
        </p>
        <p className="text-sm text-slate-600">
          {dia
            ? `Aberto por ${dia.abertoPor} às ${horaLocal(dia.abertoEmUtc)} · ${dia.caixas} caixa(s), ${dia.caixasAbertos} aberto(s)`
            : estado.exigir
              ? 'Os caixas só abrem depois de «Abrir Dia».'
              : 'Os caixas abrem sem dia (Configurações → Documentos e POS).'}
          {estado.ultimoFechado && ` · Último fecho: ${dataCurta(estado.ultimoFechado.data)} (${estado.ultimoFechado.numero})`}
        </p>
      </div>
      {estado.ultimoFechado && !dia && tem(P.caixaDia) && <BotaoImprimirDia diaId={estado.ultimoFechado.id} texto="Imprimir último fecho" />}
      {gere && !dia && (
        <button type="button" className="btn-primario" onClick={() => setDialogo('abrir')} disabled={!podeGravar}>
          <CalendarCheck className="h-4 w-4" aria-hidden /> Abrir Dia
        </button>
      )}
      {gere && dia && (
        <button type="button" className="btn-perigo" onClick={() => setDialogo('fechar')} disabled={!podeGravar}>
          <CalendarX className="h-4 w-4" aria-hidden /> Fechar Dia
        </button>
      )}
      {dialogo === 'abrir' && <AbrirDiaModal hoje={estado.hoje} aoFechar={() => setDialogo(null)} aoAbrir={() => { setDialogo(null); aoMudar() }} />}
      {dialogo === 'fechar' && dia && (
        <FecharDiaModal dia={dia} aoFechar={() => { setDialogo(null); aoMudar() }}
          aoFecharDia={(r) => { setDialogo(null); setFechado(r); aoMudar() }} />
      )}
      {fechado && (
        <Modal titulo={`Fecho do dia ${dataCurta(fechado.data)} · ${fechado.numero}`} aoFechar={() => setFechado(null)} largura="max-w-5xl"
          rodape={<><BotaoImprimirDia diaId={fechado.diaId} />
            <button type="button" className="btn-primario" onClick={() => setFechado(null)}>Fechar</button></>}>
          <ResumoDiaVista resumo={fechado} />
        </Modal>
      )}
    </div>
  )
}

function AbrirDiaModal({ hoje, aoFechar, aoAbrir }: { hoje: string; aoFechar: () => void; aoAbrir: () => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [data, setData] = useState(hoje)
  const [observacoes, setObservacoes] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aAbrir, setAAbrir] = useState(false)

  const abrir = async () => {
    setAAbrir(true)
    setErro(null)
    try {
      await api.post<DiaOperacao>('/api/caixa/dias', { data, observacoes: observacoes || null })
      avisos.sucesso(`Dia ${dataCurta(data)} aberto. Os caixas já podem abrir.`)
      aoAbrir()
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAAbrir(false)
    }
  }

  return (
    <Modal titulo="Abrir Dia" aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void abrir()} disabled={aAbrir || !podeGravar || !data}>
          {aAbrir && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Abrir Dia
        </button>
      </>}>
      <div className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <p className="text-sm text-slate-600">
          O dia de operação junta todos os caixas abertos até ao «Fechar Dia», mesmo depois da meia-noite. Cada data só se abre uma vez.
        </p>
        <div>
          <label htmlFor="dataDia" className="rotulo">Data de operação</label>
          <input id="dataDia" type="date" className="campo" value={data} max={hoje} onChange={(e) => setData(e.target.value)} />
        </div>
        <div>
          <label htmlFor="obsDia" className="rotulo">Observações (opcional)</label>
          <input id="obsDia" className="campo" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
        </div>
      </div>
    </Modal>
  )
}

function FecharDiaModal({ dia, aoFechar, aoFecharDia }: { dia: DiaOperacao; aoFechar: () => void; aoFecharDia: (r: ResumoDia) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const resumo = useDados(() => api.get<ResumoDia>(`/api/caixa/dias/${dia.id}/resumo`), [dia.id])
  const [observacoes, setObservacoes] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [confirmar, setConfirmar] = useState(false)
  const [aFechar, setAFechar] = useState(false)
  const pendencias = resumo.dados?.pendencias ?? []

  const fechar = async () => {
    setAFechar(true)
    try {
      const r = await api.post<ResumoDia>(`/api/caixa/dias/${dia.id}/fechar`, { observacoes: observacoes || null, rowVersion: dia.rowVersion })
      avisos.sucesso(`Dia ${dataCurta(r.data)} fechado (${r.numero}).`)
      void imprimirFechoAutomatico({ diaId: r.diaId }, avisos, 210)
      aoFecharDia(r)
    } catch (e) {
      setConfirmar(false)
      setErro(e instanceof ErroApi && e.estado === 409 ? `${e.message} Feche esta janela e volte a tentar.` : mensagemErro(e))
    } finally {
      setAFechar(false)
    }
  }

  return (
    <>
      <Modal titulo={`Fechar Dia ${dataCurta(dia.data)}`} aoFechar={aoFechar} largura="max-w-5xl"
        rodape={<>
          <BotaoImprimirDia diaId={dia.id} texto="Imprimir ponto de situação" />
          <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="button" className="btn-perigo" onClick={() => setConfirmar(true)} disabled={!podeGravar || !resumo.dados || pendencias.length > 0}>
            Fechar Dia
          </button>
        </>}>
        <div className="space-y-4">
          {erro && <Alerta tipo="erro">{erro}</Alerta>}
          {resumo.erro && <Alerta tipo="erro">{resumo.erro}</Alerta>}
          {!resumo.dados && !resumo.erro && <Carregando />}
          {pendencias.length > 0 && (
            <Alerta tipo="aviso" titulo="Ainda não pode fechar o dia">
              Feche primeiro: {pendencias.join('; ')}.
            </Alerta>
          )}
          {resumo.dados && resumo.dados.controlo.pedidosEmAberto > 0 && (
            <Alerta tipo="aviso">Há {resumo.dados.controlo.pedidosEmAberto} pedido(s) em aberto. Se fechar o dia, ficam para o dia seguinte.</Alerta>
          )}
          {resumo.dados && <ResumoDiaVista resumo={resumo.dados} />}
          <div>
            <label htmlFor="obsFechoDia" className="rotulo">Observações do fecho (opcional)</label>
            <textarea id="obsFechoDia" rows={2} className="campo py-2" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
          </div>
        </div>
      </Modal>
      {confirmar && resumo.dados && (
        <Confirmacao
          titulo="Confirmar fecho do dia"
          mensagem={<>
            O dia {dataCurta(dia.data)} será fechado com vendas líquidas de <strong>{formatarMoeda(resumo.dados.vendasLiquidas)}</strong> em{' '}
            {resumo.dados.caixas.length} caixa(s). O resumo fica gravado e não pode ser alterado; os caixas só voltam a abrir no próximo «Abrir Dia».
          </>}
          textoConfirmar="Confirmar fecho do dia"
          perigo
          aProcessar={aFechar}
          aoCancelar={() => setConfirmar(false)}
          aoConfirmar={() => void fechar()}
        />
      )}
    </>
  )
}

/** Resumo do dia no ecrã: indicadores, caixas, meios de pagamento, movimentos, controlo e produtos por categoria. */
export function ResumoDiaVista({ resumo: r }: { resumo: ResumoDia }) {
  const totalMetodos = r.metodos.reduce((s, m) => s + m.total, 0)
  const totalProdutos = r.categorias.reduce((s, c) => s + c.total, 0)
  const indicadores: [string, string, string?][] = [
    ['Vendas brutas', formatarMoeda(r.vendasBrutas)],
    ['Descontos', formatarMoeda(-r.descontos), r.descontos ? 'text-red-700' : ''],
    ['Vendas líquidas', formatarMoeda(r.vendasLiquidas)],
    ['Ticket médio', formatarMoeda(r.ticketMedio)],
    ['IVA incluído', formatarMoeda(r.impostos)],
    ['Total entrado', formatarMoeda(r.totalEntrado), 'text-green-700'],
    ['Total saído', formatarMoeda(r.totalSaido), r.totalSaido ? 'text-red-700' : ''],
    ['Diferença de caixa', r.diferenca === null ? '—' : comSinal(r.diferenca), corDiferenca(r.diferenca)],
  ]
  const th = 'px-2 py-2 font-medium'
  const td = 'px-2 py-1.5'
  const n = 'text-right tabular-nums'

  return (
    <div className="space-y-5 text-sm" data-testid="resumo-dia">
      <div className="flex flex-wrap gap-x-6 gap-y-1 text-slate-600">
        <span>Dia de operação <strong className="text-slate-900">{dataCurta(r.data)}</strong></span>
        {r.numero && <span>N.º <strong className="text-slate-900">{r.numero}</strong></span>}
        <span>Aberto por {r.abertoPor} em {formatarDataHora(r.abertoEmUtc)}</span>
        {r.fechadoEmUtc && <span>Fechado por {r.fechadoPor ?? '—'} em {formatarDataHora(r.fechadoEmUtc)}</span>}
        <span>{r.pedidos} pedidos pagos</span>
        {r.estado === 'Aberto' ? <Etiqueta cor="amarelo">Ponto de situação</Etiqueta> : <Etiqueta cor="verde">Dia fechado</Etiqueta>}
      </div>

      <div className="grid grid-cols-2 gap-2 md:grid-cols-4">
        {indicadores.map(([rotulo, valor, cor]) => (
          <div key={rotulo} className="rounded-lg border border-slate-200 px-3 py-2">
            <p className="text-xs uppercase tracking-wide text-slate-500">{rotulo}</p>
            <p className={`text-lg font-semibold tabular-nums ${cor ?? ''}`}>{valor}</p>
          </div>
        ))}
      </div>

      <section>
        <h3 className="mb-1 font-semibold">Resumo por caixa</h3>
        {r.caixas.length === 0 ? <p className="text-slate-500">Nenhum caixa aberto neste dia.</p> : (
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead className="border-b border-slate-300 text-slate-600"><tr>
                <th scope="col" className={th}>Caixa</th><th scope="col" className={th}>Operador</th><th scope="col" className={th}>Aberto</th>
                <th scope="col" className={th}>Fechado</th><th scope="col" className={`${th} text-right`}>Pedidos</th><th scope="col" className={`${th} text-right`}>Recebido</th>
                <th scope="col" className={`${th} text-right`}>Fundo</th><th scope="col" className={`${th} text-right`}>Esperado</th>
                <th scope="col" className={`${th} text-right`}>Contado</th><th scope="col" className={`${th} text-right`}>Diferença</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100">
                {r.caixas.map((c) => (
                  <tr key={c.sessaoId}>
                    <td className={td}>{c.terminal}</td><td className={td}>{c.operador}</td><td className={td}>{horaLocal(c.abertaEmUtc)}</td>
                    <td className={td}>{c.fechadaEmUtc ? horaLocal(c.fechadaEmUtc) : <Etiqueta cor="amarelo">Aberto</Etiqueta>}</td>
                    <td className={`${td} ${n}`}>{c.pedidos}</td><td className={`${td} ${n}`}>{formatarMoeda(c.recebido)}</td>
                    <td className={`${td} ${n}`}>{formatarMoeda(c.fundo)}</td>
                    <td className={`${td} ${n}`}>{c.esperado === null ? '—' : formatarMoeda(c.esperado)}</td>
                    <td className={`${td} ${n}`}>{c.contado === null ? '—' : formatarMoeda(c.contado)}</td>
                    <td className={`${td} ${n} font-medium ${corDiferenca(c.diferenca)}`}>{c.diferenca === null ? '—' : comSinal(c.diferenca)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section>
        <h3 className="mb-1 font-semibold">Vendas por meio de pagamento</h3>
        {r.metodos.length === 0 ? <p className="text-slate-500">Sem pagamentos.</p> : (
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead className="border-b border-slate-300 text-slate-600"><tr>
                <th scope="col" className={th}>Meio de pagamento</th>
                {r.caixas.map((c) => <th key={c.sessaoId} scope="col" className={`${th} text-right`}>{c.terminal}</th>)}
                <th scope="col" className={`${th} text-right`}>Total</th><th scope="col" className={`${th} text-right`}>%</th>
              </tr></thead>
              <tbody className="divide-y divide-slate-100">
                {r.metodos.map((m) => (
                  <tr key={m.metodo}>
                    <td className={td}>{m.metodo}</td>
                    {m.porCaixa.map((v, i) => <td key={i} className={`${td} ${n}`}>{formatarMoeda(v)}</td>)}
                    <td className={`${td} ${n} font-medium`}>{formatarMoeda(m.total)}</td><td className={`${td} ${n}`}>{pct(m.total, totalMetodos)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <div className="grid gap-5 md:grid-cols-2">
        <section>
          <h3 className="mb-1 font-semibold">Outros movimentos de caixa</h3>
          {r.movimentos.length === 0 ? <p className="text-slate-500">Sem movimentos.</p> : (
            <ul className="divide-y divide-slate-100">
              {r.movimentos.map((m) => (
                <li key={m.tipo} className="flex justify-between py-1.5">
                  <span>{NOMES_MOVIMENTO[m.tipo]} ({m.quantidade})</span>
                  <span className={`tabular-nums ${m.valor < 0 ? 'text-red-700' : 'text-green-700'}`}>{comSinal(m.valor)}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section>
          <h3 className="mb-1 font-semibold">Controlo</h3>
          <ul className="divide-y divide-slate-100">
            {([
              ['Descontos concedidos', r.controlo.descontos, r.controlo.valorDescontos],
              ['Itens anulados', r.controlo.itensAnulados, r.controlo.valorItensAnulados],
              ['Pedidos estornados', r.controlo.estornos, r.controlo.valorEstornos],
              ['Pagamentos anulados', r.controlo.pagamentosAnulados, r.controlo.valorPagamentosAnulados],
            ] as const).map(([rotulo, qtd, valor]) => (
              <li key={rotulo} className="flex justify-between py-1.5"><span>{rotulo} ({qtd})</span><span className="tabular-nums">{formatarMoeda(valor)}</span></li>
            ))}
            <li className="flex justify-between py-1.5"><span>Pedidos em aberto</span><span className={r.controlo.pedidosEmAberto ? 'font-medium text-amber-700' : ''}>{r.controlo.pedidosEmAberto}</span></li>
          </ul>
        </section>
      </div>

      <section>
        <h3 className="mb-1 font-semibold">Produtos vendidos por categoria</h3>
        {r.categorias.length === 0 ? <p className="text-slate-500">Sem vendas fechadas neste dia.</p> : (
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead className="border-b border-slate-300 text-slate-600"><tr>
                <th scope="col" className={th}>Produto</th><th scope="col" className={`${th} text-right`}>Qtd.</th>
                <th scope="col" className={`${th} text-right`}>Total</th><th scope="col" className={`${th} text-right`}>% do dia</th>
              </tr></thead>
              <tbody>
                {r.categorias.map((c) => (
                  <CategoriaLinhas key={c.categoria} categoria={c} total={totalProdutos} />
                ))}
                <tr className="border-t-2 border-slate-300 font-semibold">
                  <td className={td}>Total (antes de descontos)</td>
                  <td className={`${td} ${n}`}>{r.categorias.reduce((s, c) => s + c.quantidade, 0).toLocaleString('pt-PT')}</td>
                  <td className={`${td} ${n}`}>{formatarMoeda(totalProdutos)}</td><td className={`${td} ${n}`}>100,0</td>
                </tr>
              </tbody>
            </table>
          </div>
        )}
      </section>
      {r.observacoes && <p className="text-slate-600">Obs.: {r.observacoes}</p>}
    </div>
  )
}

function CategoriaLinhas({ categoria: c, total }: { categoria: ResumoDia['categorias'][number]; total: number }) {
  const td = 'px-2 py-1.5'
  const n = 'text-right tabular-nums'
  return (
    <>
      <tr className="bg-slate-50 font-semibold text-primaria-700">
        <td className={td}>{c.categoria}</td><td className={`${td} ${n}`}>{c.quantidade.toLocaleString('pt-PT')}</td>
        <td className={`${td} ${n}`}>{formatarMoeda(c.total)}</td><td className={`${td} ${n}`}>{pct(c.total, total)}</td>
      </tr>
      {c.produtos.map((p) => (
        <tr key={p.produto} className="border-b border-slate-100">
          <td className={`${td} pl-6`}>{p.produto}</td><td className={`${td} ${n}`}>{p.quantidade.toLocaleString('pt-PT')}</td>
          <td className={`${td} ${n}`}>{formatarMoeda(p.total)}</td><td className={`${td} ${n}`}>{pct(p.total, total)}</td>
        </tr>
      ))}
    </>
  )
}

/** Histórico dos dias de operação, com o resumo gravado de cada fecho e a reimpressão. */
export function HistoricoDias() {
  const avisos = useAvisos()
  const [pagina, setPagina] = useState(1)
  const [detalhe, setDetalhe] = useState<ResumoDia | null>(null)
  const { dados, erro, aCarregar } = useDados(() => api.get<Pagina<DiaOperacao>>(`/api/caixa/dias?pagina=${pagina}&tamanho=20`), [pagina])

  const abrir = async (id: number) => {
    try {
      setDetalhe(await api.get<ResumoDia>(`/api/caixa/dias/${id}/resumo`))
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="cartao">
      {erro && <div className="p-4"><Alerta tipo="erro">{erro}</Alerta></div>}
      {aCarregar && !dados && <Carregando />}
      {dados && dados.itens.length === 0 && <Vazio texto="Ainda não há dias de operação." />}
      {dados && dados.itens.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-600"><tr>
              <th scope="col" className="px-4 py-3">Dia</th><th scope="col" className="px-4 py-3">N.º do fecho</th>
              <th scope="col" className="px-4 py-3">Aberto por</th><th scope="col" className="px-4 py-3">Fechado</th>
              <th scope="col" className="px-4 py-3 text-right">Caixas</th><th scope="col" className="px-4 py-3">Estado</th>
            </tr></thead>
            <tbody className="divide-y divide-slate-100">
              {dados.itens.map((d) => (
                <tr key={d.id} className="cursor-pointer hover:bg-slate-50" onClick={() => void abrir(d.id)}>
                  <td className="px-4 py-2 font-medium">{dataCurta(d.data)}</td>
                  <td className="px-4 py-2">{d.numero ?? '—'}</td>
                  <td className="px-4 py-2">{d.abertoPor}</td>
                  <td className="px-4 py-2">{d.fechadoEmUtc ? `${formatarDataHora(d.fechadoEmUtc)} · ${d.fechadoPor ?? ''}` : '—'}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{d.caixas}</td>
                  <td className="px-4 py-2">{d.estado === 'Aberto' ? <Etiqueta cor="verde">Aberto</Etiqueta> : <Etiqueta cor="cinza">Fechado</Etiqueta>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {dados && dados.total > 0 && <Paginacao pagina={pagina} tamanho={20} total={dados.total} aoMudar={setPagina} />}
      {detalhe && (
        <Modal titulo={`Dia ${dataCurta(detalhe.data)}${detalhe.numero ? ` · ${detalhe.numero}` : ''}`} aoFechar={() => setDetalhe(null)} largura="max-w-5xl"
          rodape={<><BotaoImprimirDia diaId={detalhe.diaId} />
            <button type="button" className="btn-primario" onClick={() => setDetalhe(null)}>Fechar</button></>}>
          <ResumoDiaVista resumo={detalhe} />
        </Modal>
      )}
    </div>
  )
}
