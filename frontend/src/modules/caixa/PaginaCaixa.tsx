import { useCallback, useEffect, useState } from 'react'
import { ArrowDownToLine, ArrowUpFromLine, Loader2, Lock, MonitorSmartphone, Printer, Unlock } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { DialogoImprimir } from '../../components/DialogoImprimir'
import { Alerta, Carregando, Confirmacao, Etiqueta, Modal, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { MetodoPagamento, Pagina, SessaoCaixa, SessaoCaixaResumo, Terminal } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { P } from '../../utils/permissoes'
import { guardarTerminalLocal, lerTerminalLocal } from '../../utils/terminalLocal'
import { GestaoTerminais } from './GestaoTerminais'
import { ResumoSessao } from './ResumoSessao'

type EstadoSessao =
  | { tipo: 'a-carregar' }
  | { tipo: 'sem-sessao' }
  | { tipo: 'aberta'; sessao: SessaoCaixa }
  | { tipo: 'de-outro'; por: string | null }
  | { tipo: 'erro'; mensagem: string }

export function PaginaCaixa() {
  const { tem } = useSessao()
  const [separador, setSeparador] = useState<'actual' | 'historico'>('actual')
  const [gerirTerminais, setGerirTerminais] = useState(false)
  // Muda ao fechar a gestão de terminais, para recarregar a lista de terminais do ecrã.
  const [versaoTerminais, setVersaoTerminais] = useState(0)

  return (
    <div className="mx-auto max-w-7xl space-y-4 print:max-w-none">
      <div className="flex flex-wrap items-center justify-between gap-3 print:hidden">
        <h1 className="text-2xl font-semibold">Caixa</h1>
        {tem(P.configuracoesGerir) && (
          <button type="button" className="btn-secundario" onClick={() => setGerirTerminais(true)}>
            <MonitorSmartphone className="h-4 w-4" aria-hidden /> Terminais
          </button>
        )}
      </div>
      <div className="flex gap-1 border-b border-slate-200 print:hidden" role="tablist">
        {(['actual', 'historico'] as const).map((s) => (
          <button key={s} type="button" role="tab" aria-selected={separador === s} onClick={() => setSeparador(s)}
            className={`min-h-11 border-b-2 px-4 text-sm font-medium ${separador === s ? 'border-primaria-600 text-primaria-700' : 'border-transparent text-slate-600 hover:text-slate-900'}`}>
            {s === 'actual' ? 'Caixa deste terminal' : 'Histórico de sessões'}
          </button>
        ))}
      </div>
      {separador === 'actual' ? <CaixaActual key={versaoTerminais} /> : <Historico />}
      {gerirTerminais && (
        <GestaoTerminais
          aoFechar={() => {
            setGerirTerminais(false)
            setVersaoTerminais((v) => v + 1)
          }}
        />
      )}
    </div>
  )
}

/** Fecho (ou ponto de situação) da sessão em talão 80/58 mm ou A4, pelo navegador ou numa impressora do servidor. */
function BotaoImprimirFecho({ sessaoId }: { sessaoId: number }) {
  const [aberto, setAberto] = useState(false)
  return (
    <>
      <button type="button" className="btn-secundario" onClick={() => setAberto(true)}>
        <Printer className="h-4 w-4" aria-hidden /> Imprimir
      </button>
      {aberto && <DialogoImprimir tipos={['FechoCaixa']} sessaoId={sessaoId} aoFechar={() => setAberto(false)} />}
    </>
  )
}

function CaixaActual() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const [terminalId, setTerminalId] = useState<number | null>(lerTerminalLocal)
  const terminais = useDados(() => api.get<Terminal[]>('/api/terminais'), [])
  const [estado, setEstado] = useState<EstadoSessao>({ tipo: 'a-carregar' })
  const [dialogo, setDialogo] = useState<'entrada' | 'saida' | 'fechar' | null>(null)
  // Relatório do último fecho: fica visível depois de a sessão sair do ecrã.
  const [fechoConcluido, setFechoConcluido] = useState<SessaoCaixa | null>(null)

  const terminal = terminais.dados?.find((t) => t.id === terminalId) ?? null

  const carregarSessao = useCallback(async () => {
    if (!terminalId) return
    setEstado({ tipo: 'a-carregar' })
    try {
      const sessao = await api.get<SessaoCaixa | undefined>(`/api/caixa/terminais/${terminalId}/sessao-aberta`)
      setEstado(sessao ? { tipo: 'aberta', sessao } : { tipo: 'sem-sessao' })
    } catch (e) {
      if (e instanceof ErroApi && e.estado === 403) {
        const t = (await api.get<Terminal[]>('/api/terminais').catch(() => [])).find((x) => x.id === terminalId)
        setEstado({ tipo: 'de-outro', por: t?.sessaoAbertaPor ?? null })
      } else setEstado({ tipo: 'erro', mensagem: mensagemErro(e) })
    }
  }, [terminalId])

  useEffect(() => {
    void carregarSessao()
  }, [carregarSessao])

  const escolherTerminal = (id: number | null) => {
    guardarTerminalLocal(id)
    setTerminalId(id)
  }

  if (terminais.erro) return <Alerta tipo="erro">{terminais.erro}</Alerta>
  if (!terminais.dados) return <Carregando />

  if (!terminal) {
    const activos = terminais.dados.filter((t) => t.activo)
    return (
      <div className="cartao mx-auto max-w-lg p-6">
        <h2 className="text-lg font-semibold">Que terminal é este computador?</h2>
        <p className="mt-1 text-sm text-slate-600">A escolha fica guardada neste navegador. Vários operadores podem trabalhar no mesmo terminal: cada um abre e fecha a sua própria sessão de caixa.</p>
        {activos.length === 0 ? (
          <div className="mt-4">
            <Alerta tipo="info">
              Ainda não há terminais registados.{' '}
              {tem(P.configuracoesGerir) ? 'Use o botão «Terminais» para criar o primeiro.' : 'Peça ao administrador para os criar.'}
            </Alerta>
          </div>
        ) : (
          <div className="mt-4 grid gap-2 sm:grid-cols-2">
            {activos.map((t) => (
              <button key={t.id} type="button" className="btn-secundario min-h-14 justify-start" onClick={() => escolherTerminal(t.id)}>
                <MonitorSmartphone className="h-5 w-5" aria-hidden />
                <span className="text-left">
                  {t.nome}
                  {t.sessaoAbertaPor && <span className="block text-xs text-slate-500">Sessões abertas: {t.sessaoAbertaPor}</span>}
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3 print:hidden">
        <span className="text-sm text-slate-600">
          Terminal: <strong className="text-slate-800">{terminal.nome}</strong>
        </span>
        <button type="button" className="text-sm font-medium text-primaria-700 hover:underline" onClick={() => escolherTerminal(null)}>
          Mudar terminal
        </button>
      </div>

      {estado.tipo === 'a-carregar' && <Carregando />}
      {estado.tipo === 'erro' && <Alerta tipo="erro">{estado.mensagem}</Alerta>}
      {estado.tipo === 'de-outro' && (
        <Alerta tipo="aviso" titulo="Caixa em uso">
          Este terminal tem uma sessão aberta por {estado.por ?? 'outro utilizador'}. Só essa pessoa, ou um gerente, a pode movimentar ou fechar.
        </Alerta>
      )}
      {estado.tipo === 'sem-sessao' &&
        (tem(P.caixaOperar) ? (
          <AbrirCaixa terminal={terminal} aoAbrir={(s) => setEstado({ tipo: 'aberta', sessao: s })} />
        ) : (
          <Alerta tipo="info">Não tem sessão de caixa aberta neste terminal.</Alerta>
        ))}

      {estado.tipo === 'aberta' && (
        <>
          <div className="flex flex-wrap gap-2 print:hidden">
            {tem(P.caixaMovimentos) && (
              <>
                <button type="button" className="btn-secundario" onClick={() => setDialogo('entrada')} disabled={!podeGravar}>
                  <ArrowDownToLine className="h-4 w-4 text-green-700" aria-hidden /> Entrada
                </button>
                <button type="button" className="btn-secundario" onClick={() => setDialogo('saida')} disabled={!podeGravar}>
                  <ArrowUpFromLine className="h-4 w-4 text-red-700" aria-hidden /> Saída
                </button>
              </>
            )}
            <BotaoImprimirFecho sessaoId={estado.sessao.id} />
            {tem(P.caixaOperar) && (
              <button type="button" className="btn-perigo ml-auto" onClick={() => setDialogo('fechar')} disabled={!podeGravar}>
                <Lock className="h-4 w-4" aria-hidden /> Fechar caixa
              </button>
            )}
          </div>
          <ResumoSessao sessao={estado.sessao} />
          {(dialogo === 'entrada' || dialogo === 'saida') && (
            <MovimentoModal
              sessao={estado.sessao}
              tipo={dialogo}
              aoFechar={() => setDialogo(null)}
              aoGuardar={(s) => {
                setEstado({ tipo: 'aberta', sessao: s })
                setDialogo(null)
              }}
            />
          )}
          {dialogo === 'fechar' && (
            <FecharModal
              sessao={estado.sessao}
              aoFechar={() => setDialogo(null)}
              aoFecharSessao={(s) => {
                setDialogo(null)
                setEstado({ tipo: 'sem-sessao' })
                void terminais.recarregar()
                setFechoConcluido(s)
              }}
            />
          )}
        </>
      )}
      {fechoConcluido && (
        <Modal titulo="Relatório de fecho de caixa" aoFechar={() => setFechoConcluido(null)} largura="max-w-4xl"
          rodape={<><BotaoImprimirFecho sessaoId={fechoConcluido.id} />
            <button type="button" className="btn-primario" onClick={() => setFechoConcluido(null)}>Fechar</button></>}>
          <ResumoSessao sessao={fechoConcluido} />
        </Modal>
      )}
    </div>
  )
}

function AbrirCaixa({ terminal, aoAbrir }: { terminal: Terminal; aoAbrir: (s: SessaoCaixa) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [fundo, setFundo] = useState('')
  const [observacoes, setObservacoes] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aAbrir, setAAbrir] = useState(false)

  const abrir = async () => {
    const valor = Number(fundo === '' ? 0 : fundo)
    if (!Number.isFinite(valor) || valor < 0) return setErro('Indique um fundo inicial válido (zero ou positivo).')
    setErro(null)
    setAAbrir(true)
    try {
      const sessao = await api.post<SessaoCaixa>('/api/caixa/sessoes', { terminalId: terminal.id, fundoInicial: valor, observacoes: observacoes || null })
      avisos.sucesso('Caixa aberto.')
      aoAbrir(sessao)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAAbrir(false)
    }
  }

  return (
    <div className="cartao max-w-lg p-6">
      <h2 className="flex items-center gap-2 text-lg font-semibold">
        <Unlock className="h-5 w-5 text-primaria-600" aria-hidden /> Abrir caixa
      </h2>
      <p className="mt-1 text-sm text-slate-600">Conte o dinheiro na gaveta e indique o fundo inicial (trocos).</p>
      <div className="mt-4 space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div>
          <label htmlFor="fundo" className="rotulo">Fundo inicial (MT)</label>
          <input id="fundo" type="number" inputMode="decimal" min={0} step="0.01" className="campo text-lg" value={fundo} onChange={(e) => setFundo(e.target.value)} placeholder="0,00" />
        </div>
        <div>
          <label htmlFor="obsAbertura" className="rotulo">Observações (opcional)</label>
          <input id="obsAbertura" className="campo" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
        </div>
        <button type="button" className="btn-primario w-full" onClick={() => void abrir()} disabled={aAbrir || !podeGravar}>
          {aAbrir && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Abrir caixa
        </button>
      </div>
    </div>
  )
}

function MovimentoModal({ sessao, tipo, aoFechar, aoGuardar }: {
  sessao: SessaoCaixa
  tipo: 'entrada' | 'saida'
  aoFechar: () => void
  aoGuardar: (s: SessaoCaixa) => void
}) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const { dados: metodos } = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  const [metodoId, setMetodoId] = useState<number | null>(null)
  const [valor, setValor] = useState('')
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const metodo = metodoId ?? metodos?.find((m) => m.contaNaGaveta)?.id ?? null

  const guardar = async () => {
    const v = Number(valor)
    if (!metodo) return setErro('Seleccione o método.')
    if (!Number.isFinite(v) || v <= 0) return setErro('Indique um valor positivo.')
    if (motivo.trim().length < 3) return setErro('Indique o motivo (pelo menos 3 caracteres).')
    setErro(null)
    setAGuardar(true)
    try {
      const s = await api.post<SessaoCaixa>(`/api/caixa/sessoes/${sessao.id}/movimentos`, {
        tipo: tipo === 'entrada' ? 'Entrada' : 'Saida',
        metodoPagamentoId: metodo,
        valor: v,
        motivo: motivo.trim(),
      })
      avisos.sucesso(tipo === 'entrada' ? 'Entrada registada.' : 'Saída registada.')
      aoGuardar(s)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={tipo === 'entrada' ? 'Entrada de caixa' : 'Saída de caixa'} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Registar
        </button>
      </>}>
      <div className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <p className="text-sm text-slate-600">
          {tipo === 'entrada' ? 'Ex.: reforço de trocos.' : 'Ex.: sangria para o cofre. Despesas pagas pela caixa registam-se no módulo Despesas.'}
        </p>
        <div>
          <label htmlFor="metodoMov" className="rotulo">Método</label>
          <select id="metodoMov" className="campo" value={metodo ?? ''} onChange={(e) => setMetodoId(Number(e.target.value))}>
            {metodos?.filter((m) => m.activo).map((m) => <option key={m.id} value={m.id}>{m.nome}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="valorMov" className="rotulo">Valor (MT)</label>
          <input id="valorMov" type="number" inputMode="decimal" min={0} step="0.01" className="campo text-lg" value={valor} onChange={(e) => setValor(e.target.value)} />
        </div>
        <div>
          <label htmlFor="motivoMov" className="rotulo">Motivo</label>
          <input id="motivoMov" className="campo" maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </div>
      </div>
    </Modal>
  )
}

function FecharModal({ sessao, aoFechar, aoFecharSessao }: { sessao: SessaoCaixa; aoFechar: () => void; aoFecharSessao: (s: SessaoCaixa) => void }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  // Fecho cego: o servidor diz o que contar, mas não quanto devia haver.
  const linhas = sessao.porMetodo.filter((m) => m.contar)
  const [contagens, setContagens] = useState<Record<number, string>>({})
  const [observacoes, setObservacoes] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [confirmar, setConfirmar] = useState(false)
  const [aFechar, setAFechar] = useState(false)

  const valorContado = (id: number) => (contagens[id] === undefined || contagens[id] === '' ? null : Number(contagens[id]))
  const emFalta = linhas.filter((l) => valorContado(l.metodoPagamentoId) === null)
  const invalidos = linhas.filter((l) => {
    const v = valorContado(l.metodoPagamentoId)
    return v !== null && (!Number.isFinite(v) || v < 0)
  })

  const pedirConfirmacao = () => {
    if (emFalta.length > 0) return setErro(`Indique o valor contado em: ${emFalta.map((l) => l.metodo).join(', ')}.`)
    if (invalidos.length > 0) return setErro('Há valores contados inválidos.')
    setErro(null)
    setConfirmar(true)
  }

  const fechar = async () => {
    setAFechar(true)
    try {
      const s = await api.post<SessaoCaixa>(`/api/caixa/sessoes/${sessao.id}/fechar`, {
        contagens: linhas.map((l) => ({ metodoPagamentoId: l.metodoPagamentoId, valorContado: valorContado(l.metodoPagamentoId) ?? 0 })),
        observacoes: observacoes || null,
        rowVersion: sessao.rowVersion,
      })
      avisos.sucesso('Caixa fechado.')
      aoFecharSessao(s)
    } catch (e) {
      setConfirmar(false)
      setErro(e instanceof ErroApi && e.estado === 409 ? `${e.message} Feche esta janela para ver os valores actualizados.` : mensagemErro(e))
    } finally {
      setAFechar(false)
    }
  }

  return (
    <>
      <Modal titulo="Fechar caixa" aoFechar={aoFechar} largura="max-w-2xl"
        rodape={<>
          <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="button" className="btn-perigo" onClick={pedirConfirmacao} disabled={!podeGravar}>Fechar caixa</button>
        </>}>
        <div className="space-y-4">
          {erro && <Alerta tipo="erro">{erro}</Alerta>}
          <p className="text-sm text-slate-600">
            Conte o dinheiro e os comprovativos de cada método e introduza o que contou. O valor esperado e as diferenças só aparecem
            depois de confirmar o fecho, no relatório; os valores contados não podem ser alterados depois.
          </p>
          <table className="w-full text-sm">
            <thead className="text-left text-slate-600">
              <tr>
                <th scope="col" className="py-2">Método</th>
                <th scope="col" className="py-2 text-right">Valor contado</th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {linhas.map((l) => (
                <tr key={l.metodoPagamentoId} className="border-t border-slate-100">
                  <td className="py-2 font-medium">{l.metodo}</td>
                  <td className="py-2 pl-4">
                    <input type="number" inputMode="decimal" min={0} step="0.01" className="campo text-right" aria-label={`Valor contado em ${l.metodo}`}
                      value={contagens[l.metodoPagamentoId] ?? ''} onChange={(e) => setContagens((c) => ({ ...c, [l.metodoPagamentoId]: e.target.value }))} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div>
            <label htmlFor="obsFecho" className="rotulo">Observações (ex.: justificação de diferenças)</label>
            <textarea id="obsFecho" rows={2} className="campo py-2" maxLength={1000} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
          </div>
        </div>
      </Modal>
      {confirmar && (
        <Confirmacao
          titulo="Confirmar fecho de caixa"
          mensagem={<>
            A sessão será fechada com os valores contados:{' '}
            <strong>{linhas.map((l) => `${l.metodo} ${formatarMoeda(valorContado(l.metodoPagamentoId) ?? 0)}`).join(' · ')}</strong>.
            Esta operação não pode ser desfeita; a seguir aparece o relatório com o valor esperado e as diferenças.
          </>}
          textoConfirmar="Confirmar fecho"
          perigo
          aProcessar={aFechar}
          aoCancelar={() => setConfirmar(false)}
          aoConfirmar={() => void fechar()}
        />
      )}
    </>
  )
}

function Historico() {
  const [pagina, setPagina] = useState(1)
  const [detalhe, setDetalhe] = useState<SessaoCaixa | null>(null)
  const avisos = useAvisos()
  const { dados, erro, aCarregar } = useDados(() => api.get<Pagina<SessaoCaixaResumo>>(`/api/caixa/sessoes?pagina=${pagina}&tamanho=20`), [pagina])

  const abrirDetalhe = async (id: number) => {
    try {
      setDetalhe(await api.get<SessaoCaixa>(`/api/caixa/sessoes/${id}`))
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="cartao">
      {erro && <div className="p-4"><Alerta tipo="erro">{erro}</Alerta></div>}
      {aCarregar && !dados && <Carregando />}
      {dados && dados.itens.length === 0 && <Vazio texto="Ainda não há sessões de caixa." />}
      {dados && dados.itens.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-600">
              <tr>
                <th scope="col" className="px-4 py-3">Abertura</th>
                <th scope="col" className="px-4 py-3">Fecho</th>
                <th scope="col" className="px-4 py-3">Terminal</th>
                <th scope="col" className="px-4 py-3">Operador</th>
                <th scope="col" className="px-4 py-3 text-right">Fundo</th>
                <th scope="col" className="px-4 py-3 text-right">Diferença</th>
                <th scope="col" className="px-4 py-3">Estado</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {dados.itens.map((s) => (
                <tr key={s.id} className="cursor-pointer hover:bg-slate-50" onClick={() => void abrirDetalhe(s.id)}>
                  <td className="px-4 py-2">{formatarDataHora(s.abertaEmUtc)}</td>
                  <td className="px-4 py-2">{s.fechadaEmUtc ? formatarDataHora(s.fechadaEmUtc) : '—'}</td>
                  <td className="px-4 py-2">{s.terminal}</td>
                  <td className="px-4 py-2">{s.abertaPor}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{formatarMoeda(s.fundoInicial)}</td>
                  <td className={`px-4 py-2 text-right font-medium tabular-nums ${s.totalDiferenca ? (s.totalDiferenca > 0 ? 'text-green-700' : 'text-red-700') : ''}`}>
                    {s.totalDiferenca === null ? '—' : formatarMoeda(s.totalDiferenca)}
                  </td>
                  <td className="px-4 py-2">{s.estado === 'Aberta' ? <Etiqueta cor="verde">Aberta</Etiqueta> : <Etiqueta cor="cinza">Fechada</Etiqueta>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {dados && dados.total > 0 && <Paginacao pagina={pagina} tamanho={20} total={dados.total} aoMudar={setPagina} />}
      {detalhe && (
        <Modal titulo={`Sessão de caixa n.º ${detalhe.id}`} aoFechar={() => setDetalhe(null)} largura="max-w-4xl"
          rodape={<><BotaoImprimirFecho sessaoId={detalhe.id} />
            <button type="button" className="btn-primario" onClick={() => setDetalhe(null)}>Fechar</button></>}>
          <ResumoSessao sessao={detalhe} />
        </Modal>
      )}
    </div>
  )
}
