import { useState } from 'react'
import { AlertTriangle, CheckCircle2, Loader2, Pencil, Plus, Power, Printer } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Configuracao, EstacaoPreparacao, FuncaoImpressora, Impressora, LocalImpressoras, ModoImpressora, Terminal } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'

const FUNCOES: { valor: FuncaoImpressora; rotulo: string; ajuda: string }[] = [
  { valor: 'Caixa', rotulo: 'Caixa', ajuda: 'Recibos dos pagamentos' },
  { valor: 'Bar', rotulo: 'Bar', ajuda: 'Talões das bebidas e produtos do bar' },
  { valor: 'Cozinha', rotulo: 'Cozinha', ajuda: 'Talões dos pratos enviados à cozinha' },
  { valor: 'Producao', rotulo: 'Outro centro', ajuda: 'Pastelaria, grelhados…: só o que a categoria ou o produto lhe enviar' },
]
const PAPEIS = [
  { valor: 80, rotulo: '80 mm', ajuda: 'Talão térmico largo' },
  { valor: 58, rotulo: '58 mm', ajuda: 'Talão térmico estreito' },
  { valor: 210, rotulo: 'A4', ajuda: 'Impressora de escritório' },
]
const MODOS: { valor: ModoImpressora; rotulo: string; ajuda: string }[] = [
  { valor: 'Directo', rotulo: 'Imprimir directo', ajuda: 'Sai na impressora do Windows (do servidor ou do posto), sem janelas' },
  { valor: 'Pdf', rotulo: 'Abrir em PDF', ajuda: 'O documento abre em PDF no ecrã de quem o pediu, para imprimir ou guardar' },
]
const COR_FUNCAO: Record<FuncaoImpressora, 'azul' | 'amarelo' | 'verde' | 'cinza'> = { Caixa: 'azul', Bar: 'amarelo', Cozinha: 'verde', Producao: 'cinza', Etiquetas: 'cinza' }

const descreverPapel =(mm: number) => PAPEIS.find((p) => p.valor === mm)?.rotulo ?? `${mm} mm`

/**
 * Impressoras que imprimem recibos e talões sem diálogo: instaladas no Windows do servidor, ou num posto com o agente de impressão
 * (cada posto usa as impressoras instaladas no seu computador).
 */
export function Impressoras() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  // A impressora de etiquetas configura-se no separador Etiquetas.
  const lista = useDados(() => api.get<Impressora[]>('/api/configuracoes/impressoras').then((l) => l.filter((i) => i.funcao !== 'Etiquetas')), [])
  const config = useDados(() => api.get<Configuracao[]>('/api/configuracoes'), [])
  const [editar, setEditar] = useState<Impressora | 'nova' | null>(null)
  const reciboAutomatico = config.dados?.find((c) => c.chave === 'impressao.recibo-automatico')?.valor !== 'false'

  const accao = async (fazer: () => Promise<unknown>, ok: string) => {
    try {
      await fazer()
      avisos.sucesso(ok)
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const testar = (i: Impressora) => accao(async () => {
    await api.post(`/api/configuracoes/impressoras/${i.id}/teste`, {})
    // O teste corre em segundo plano: actualiza o estado pouco depois.
    window.setTimeout(() => void lista.recarregar(), 2500)
  }, i.modo === 'Pdf' ? `Página de teste de ${i.nome}: o PDF abre dentro de momentos.` : `Página de teste enviada para ${i.nome}.`)

  const alterarReciboAutomatico = (activo: boolean) => accao(async () => {
    config.setDados(await api.put<Configuracao[]>('/api/configuracoes', { valores: { 'impressao.recibo-automatico': activo ? 'true' : 'false' } }))
  }, activo ? 'O recibo passa a ser impresso ao fechar o pedido.' : 'O recibo deixa de ser impresso automaticamente.')

  return (
    <div className="space-y-4">
      <section className="cartao p-4">
        <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="font-semibold">Impressoras</h2>
            <p className="max-w-3xl text-sm text-slate-600">
              Os documentos saem directamente, sem janelas: talões da <strong>cozinha</strong> e do <strong>bar</strong> ao carregar em «Enviar» no POS,
              e o <strong>recibo</strong> na impressora do caixa quando o pedido fica pago. Cada impressora está <strong>ligada ao servidor</strong> ou
              a um <strong>posto</strong>: um posto usa as impressoras instaladas no seu próprio Windows (USB ou rede), através do agente de impressão
              instalado com o LAMGest-Setup (modo Posto).
              Em modo <strong>PDF</strong>, o documento abre no ecrã de quem o pediu, para imprimir ou guardar (não precisa de impressora).
            </p>
          </div>
          <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setEditar('nova')}>
            <Plus className="h-4 w-4" aria-hidden /> Nova impressora
          </button>
        </div>
        {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
        {!lista.dados && !lista.erro && <Carregando />}
        {lista.dados?.length === 0 && <Vazio texto="Ainda não há impressoras. Sem elas, os recibos imprimem-se pela janela de impressão do navegador e a cozinha usa só o ecrã." />}
        {lista.dados && lista.dados.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Nome</th>
                  <th scope="col" className="py-2">Função</th>
                  <th scope="col" className="py-2">Saída</th>
                  <th scope="col" className="py-2">Papel</th>
                  <th scope="col" className="py-2">Imprime</th>
                  <th scope="col" className="py-2">Estado</th>
                  <th scope="col" className="py-2 text-right">Acções</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {lista.dados.map((i) => (
                  <tr key={i.id} className={i.activo ? '' : 'opacity-60'}>
                    <td className="py-2 font-medium">{i.nome}{!i.activo && <span className="ml-2"><Etiqueta cor="cinza">Inactiva</Etiqueta></span>}</td>
                    <td className="py-2"><Etiqueta cor={COR_FUNCAO[i.funcao]}>{FUNCOES.find((f) => f.valor === i.funcao)?.rotulo ?? i.funcao}</Etiqueta></td>
                    <td className="py-2 text-slate-700">
                      {i.modo === 'Pdf' ? <Etiqueta cor="amarelo">PDF</Etiqueta> : (
                        <>
                          <span className="block text-xs text-slate-500">{i.posto ? `Posto ${i.posto}` : 'Servidor'}</span>
                          {i.nomeWindows}
                        </>
                      )}
                    </td>
                    <td className="whitespace-nowrap py-2">{descreverPapel(i.larguraPapelMm)}</td>
                    <td className="py-2 text-slate-600">
                      {i.funcao === 'Caixa' ? (i.terminal ? `Recibos do terminal ${i.terminal}` : 'Recibos (todos os terminais sem impressora própria)')
                        : i.estacao ? `Só a estação ${i.estacao}` : i.funcao === 'Bar' ? 'Produtos do tipo Bar' : i.funcao === 'Producao' ? 'Só as categorias/produtos que a escolham' : 'Pratos que vão à cozinha'}
                    </td>
                    <td className="py-2">
                      {i.ultimoErro ? (
                        <span className="inline-flex items-center gap-1 text-red-700" title={i.ultimaImpressaoUtc ? formatarDataHora(i.ultimaImpressaoUtc) : undefined}>
                          <AlertTriangle className="h-4 w-4" aria-hidden /> {i.ultimoErro}
                        </span>
                      ) : i.ultimaImpressaoUtc ? (
                        <span className="inline-flex items-center gap-1 text-green-700"><CheckCircle2 className="h-4 w-4" aria-hidden /> {formatarDataHora(i.ultimaImpressaoUtc)}</span>
                      ) : <span className="text-slate-400">—</span>}
                    </td>
                    <td className="py-2">
                      <div className="flex justify-end gap-1">
                        <button type="button" className="btn-icone" title="Imprimir página de teste" aria-label={`Testar ${i.nome}`} disabled={!podeGravar} onClick={() => void testar(i)}>
                          <Printer className="h-4 w-4" />
                        </button>
                        <button type="button" className="btn-icone" title="Editar" aria-label={`Editar ${i.nome}`} disabled={!podeGravar} onClick={() => setEditar(i)}>
                          <Pencil className="h-4 w-4" />
                        </button>
                        <button type="button" className="btn-icone" title={i.activo ? 'Desactivar' : 'Activar'} aria-label={`${i.activo ? 'Desactivar' : 'Activar'} ${i.nome}`} disabled={!podeGravar}
                          onClick={() => void accao(() => api.post(`/api/configuracoes/impressoras/${i.id}/activo`, { activo: !i.activo }), i.activo ? 'Impressora desactivada.' : 'Impressora activada.')}>
                          <Power className={`h-4 w-4 ${i.activo ? 'text-red-600' : 'text-green-700'}`} />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="cartao p-4">
        <h2 className="mb-2 font-semibold">Recibos</h2>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" checked={reciboAutomatico} disabled={!podeGravar || !config.dados} onChange={(e) => void alterarReciboAutomatico(e.target.checked)} />
          Imprimir o recibo automaticamente na impressora do caixa quando o pedido fica pago
        </label>
        <p className="text-xs text-slate-500">Desligado, o recibo só sai ao carregar em «Imprimir recibo» no POS. Sem impressora de caixa configurada, o POS usa a janela de impressão do navegador.</p>
      </section>

      {editar && <FormImpressora impressora={editar === 'nova' ? null : editar} aoFechar={(gravou) => { setEditar(null); if (gravou) void lista.recarregar() }} />}
    </div>
  )
}

function FormImpressora({ impressora, aoFechar }: { impressora: Impressora | null; aoFechar: (gravou: boolean) => void }) {
  const avisos = useAvisos()
  const locais = useDados(() => api.get<LocalImpressoras[]>('/api/configuracoes/impressoras/locais'), [])
  const terminais = useDados(() => api.get<Terminal[]>('/api/terminais'), [])
  const estacoes = useDados(() => api.get<EstacaoPreparacao[]>('/api/catalogo/estacoes'), [])
  const [nome, setNome] = useState(impressora?.nome ?? '')
  const [funcao, setFuncao] = useState<FuncaoImpressora>(impressora?.funcao ?? 'Cozinha')
  const [nomeWindows, setNomeWindows] = useState(impressora?.nomeWindows ?? '')
  const [largura, setLargura] = useState(impressora?.larguraPapelMm ?? 80)
  const [modo, setModo] = useState<ModoImpressora>(impressora?.modo ?? 'Directo')
  const [terminalId, setTerminalId] = useState<number | null>(impressora?.terminalId ?? null)
  const [estacaoId, setEstacaoId] = useState<number | null>(impressora?.estacaoPreparacaoId ?? null)
  const [postoId, setPostoId] = useState<number | null>(impressora?.postoId ?? null)
  const [cortar, setCortar] = useState(impressora?.cortarPapel ?? true)
  const [gaveta, setGaveta] = useState<boolean | null>(impressora?.abrirGaveta ?? null)
  const abrirGaveta = funcao === 'Caixa' && (gaveta ?? true) // nova impressora de pagamentos: abre a gaveta por omissão
  const local = locais.dados?.find((l) => l.postoId === postoId)
  const instaladas = local?.impressoras
  const [aGuardar, setAGuardar] = useState(false)

  // A impressora gravada pode ter sido removida do Windows: continua a aparecer para se ver o problema.
  const mesmoLocal = (impressora?.postoId ?? null) === postoId
  const opcoesWindows = instaladas ? [...new Set([...instaladas, ...(mesmoLocal && impressora?.nomeWindows ? [impressora.nomeWindows] : [])])] : []
  const naoInstalada = mesmoLocal && !!impressora?.nomeWindows && !!instaladas && !instaladas.includes(impressora.nomeWindows) && nomeWindows === impressora.nomeWindows
  const ondeTexto = postoId === null ? 'no Windows do servidor' : `no posto ${local?.nome ?? ''}`

  const mudarLocal = (valor: string) => {
    const novo = valor === '' ? null : Number(valor)
    setPostoId(novo)
    setNomeWindows('')
    // Recibos: a impressora de um posto imprime, por omissão, os recibos pagos nesse terminal.
    if (novo !== null && funcao === 'Caixa' && terminalId === null) setTerminalId(novo)
  }
  // Em PDF a impressora do Windows é opcional.
  const valido = !!nome.trim() && (modo === 'Pdf' || !!nomeWindows)

  const guardar = async () => {
    setAGuardar(true)
    try {
      const corpo = {
        nome: nome.trim(), funcao, nomeWindows: nomeWindows || null, larguraPapelMm: largura, modo,
        postoId: modo === 'Pdf' ? null : postoId,
        terminalId: funcao === 'Caixa' ? terminalId : null,
        estacaoPreparacaoId: funcao === 'Caixa' ? null : estacaoId,
        cortarPapel: cortar, abrirGaveta,
      }
      if (impressora) await api.put(`/api/configuracoes/impressoras/${impressora.id}`, corpo)
      else await api.post('/api/configuracoes/impressoras', corpo)
      avisos.sucesso(impressora ? 'Impressora actualizada.' : 'Impressora criada. Use «Testar» para confirmar.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={impressora ? `Editar ${impressora.nome}` : 'Nova impressora'} aoFechar={() => aoFechar(false)} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar(false)}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aGuardar || !valido} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="space-y-4">
        <div>
          <label htmlFor="impNome" className="rotulo">Nome</label>
          <input id="impNome" className="campo" maxLength={100} value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: Cozinha quente, Bar, Caixa 1" />
        </div>

        <fieldset>
          <legend className="rotulo">Função</legend>
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
            {FUNCOES.map((f) => (
              <label key={f.valor} className={`flex min-h-16 cursor-pointer flex-col justify-center rounded-lg border-2 px-3 py-2 text-sm ${funcao === f.valor ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
                <span className="flex items-center gap-2 font-medium">
                  <input type="radio" name="funcao" className="h-4 w-4" checked={funcao === f.valor} onChange={() => setFuncao(f.valor)} /> {f.rotulo}
                </span>
                <span className="text-xs text-slate-500">{f.ajuda}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <fieldset>
          <legend className="rotulo">Modo de impressão</legend>
          <div className="grid gap-2 sm:grid-cols-2">
            {MODOS.map((m) => (
              <label key={m.valor} className={`flex min-h-16 cursor-pointer flex-col justify-center rounded-lg border-2 px-3 py-2 text-sm ${modo === m.valor ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
                <span className="flex items-center gap-2 font-medium">
                  <input type="radio" name="modo" className="h-4 w-4" checked={modo === m.valor} onChange={() => setModo(m.valor)} /> {m.rotulo}
                </span>
                <span className="text-xs text-slate-500">{m.ajuda}</span>
              </label>
            ))}
          </div>
        </fieldset>

        {modo === 'Directo' && (
          <div>
            <label htmlFor="impLocal" className="rotulo">Ligada a</label>
            {locais.erro && <Alerta tipo="erro">{locais.erro}</Alerta>}
            <select id="impLocal" className="campo" value={postoId ?? ''} onChange={(e) => mudarLocal(e.target.value)} disabled={!locais.dados}>
              {!locais.dados && <option value="">A carregar…</option>}
              {locais.dados?.map((l) => (
                <option key={l.postoId ?? 'servidor'} value={l.postoId ?? ''} disabled={l.postoId !== null && !l.temAgente}>
                  {l.postoId === null ? 'Servidor' : `Posto ${l.numero} — ${l.nome}${!l.temAgente ? ' (sem agente de impressão)' : !l.ligado ? ' (agente desligado)' : ''}`}
                </option>
              ))}
            </select>
            {local && local.postoId !== null && !local.ligado && (
              <p className="mt-1 text-xs text-amber-700">
                O agente deste posto não comunica {local.vistoEmUtc ? `desde ${formatarDataHora(local.vistoEmUtc)}` : 'ainda'}: a lista pode estar desactualizada e os documentos só saem quando o computador estiver ligado.
              </p>
            )}
            {locais.dados && locais.dados.every((l) => l.postoId === null || !l.temAgente) && (
              <p className="mt-1 text-xs text-slate-500">Para usar uma impressora ligada a um posto (ex.: USB), instale nesse computador o agente de impressão: LAMGest-Setup → Posto → «Agente de impressão».</p>
            )}
          </div>
        )}

        <div>
          <label htmlFor="impWindows" className="rotulo">Impressora do Windows ({postoId === null || modo === 'Pdf' ? 'no servidor' : `no posto ${local?.nome ?? ''}`}){modo === 'Pdf' && ' — opcional em PDF'}</label>
          {instaladas?.length === 0 && modo === 'Directo' && <Alerta tipo="aviso">Não há impressoras instaladas {ondeTexto}. Instale-as primeiro{postoId === null ? ' (para todos os utilizadores)' : ''} ou escolha «Abrir em PDF».</Alerta>}
          <select id="impWindows" className="campo" value={nomeWindows} onChange={(e) => setNomeWindows(e.target.value)} disabled={!instaladas}>
            <option value="">{!instaladas ? 'A carregar…' : modo === 'Pdf' ? 'Nenhuma' : 'Escolha…'}</option>
            {opcoesWindows.map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
          {naoInstalada && <p className="mt-1 text-xs text-red-700">Esta impressora já não está instalada {ondeTexto}.</p>}
        </div>

        <fieldset>
          <legend className="rotulo">Papel</legend>
          <div className="grid gap-2 sm:grid-cols-3">
            {PAPEIS.map((p) => (
              <label key={p.valor} className={`flex min-h-14 cursor-pointer flex-col justify-center rounded-lg border-2 px-3 py-2 text-sm ${largura === p.valor ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
                <span className="flex items-center gap-2 font-medium">
                  <input type="radio" name="papel" className="h-4 w-4" checked={largura === p.valor} onChange={() => setLargura(p.valor)} /> {p.rotulo}
                </span>
                <span className="text-xs text-slate-500">{p.ajuda}</span>
              </label>
            ))}
          </div>
        </fieldset>

        {modo === 'Directo' && largura < 210 && funcao !== 'Etiquetas' && (
          <fieldset className="space-y-2 rounded-lg border border-slate-200 p-3">
            <legend className="px-1 text-sm font-medium">Comandos da impressora térmica</legend>
            <label className="flex items-start gap-2 text-sm">
              <input type="checkbox" className="mt-0.5 h-4 w-4" checked={cortar} onChange={(e) => setCortar(e.target.checked)} />
              <span>Cortar o papel no fim de cada talão<span className="block text-xs text-slate-500">Desligue só se o controlador da impressora já corta (para não sair um pedaço de papel em branco).</span></span>
            </label>
            {funcao === 'Caixa' && (
              <label className="flex items-start gap-2 text-sm">
                <input type="checkbox" className="mt-0.5 h-4 w-4" checked={abrirGaveta} onChange={(e) => setGaveta(e.target.checked)} />
                <span>Abrir a gaveta do dinheiro em cada pagamento confirmado<span className="block text-xs text-slate-500">A gaveta tem de estar ligada à impressora. O POS tem também o botão «Abrir gaveta».</span></span>
              </label>
            )}
          </fieldset>
        )}

        {funcao === 'Caixa' ? (
          <div>
            <label htmlFor="impTerminal" className="rotulo">Terminal de caixa</label>
            <select id="impTerminal" className="campo" value={terminalId ?? ''} onChange={(e) => setTerminalId(e.target.value ? Number(e.target.value) : null)}>
              <option value="">Todos os terminais sem impressora própria</option>
              {terminais.dados?.map((t) => <option key={t.id} value={t.id}>{t.numero ? `Posto ${t.numero} — ` : ''}{t.nome}</option>)}
            </select>
            <p className="mt-1 text-xs text-slate-500">Com vários caixas, crie uma impressora por terminal: cada recibo sai na impressora do caixa onde foi pago.</p>
          </div>
        ) : (
          <div>
            <label htmlFor="impEstacao" className="rotulo">Estação de preparação</label>
            <select id="impEstacao" className="campo" value={estacaoId ?? ''} onChange={(e) => setEstacaoId(e.target.value ? Number(e.target.value) : null)}>
              <option value="">{funcao === 'Producao' ? 'Nenhuma (recebe pelas categorias/produtos)' : `Todas (${funcao === 'Bar' ? 'produtos do tipo Bar' : 'pratos que vão à cozinha'})`}</option>
              {estacoes.dados?.filter((e) => e.activo).map((e) => <option key={e.id} value={e.id}>{e.nome}</option>)}
            </select>
            <p className="mt-1 text-xs text-slate-500">Com uma estação escolhida, esta impressora recebe só os produtos das categorias dessa estação (ex.: grelhados, pastelaria).</p>
          </div>
        )}
      </div>
    </Modal>
  )
}
