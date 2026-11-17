import { useState } from 'react'
import { Copy, ExternalLink, Loader2, MonitorSmartphone, RefreshCw } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { PostoEstado, ProtocoloVisor, TipoVisor, VisorPosto } from '../../types/api'

const PROTOCOLOS: { valor: ProtocoloVisor; nome: string }[] = [
  { valor: 'EscPos', nome: 'ESC/POS (Epson e compatíveis)' },
  { valor: 'Cd5220', nome: 'CD5220' },
  { valor: 'Simples', nome: 'Texto simples' },
]
const VELOCIDADES = [2400, 4800, 9600, 19200, 38400, 57600, 115200]
const COLUNAS = [16, 20, 40]

const nomeEcra = (nome: string) => nome.replace(/^\\\\\.\\/, '')

/** Linha do visor no cartão do posto. */
export function ResumoVisor({ posto: p }: { posto: PostoEstado }) {
  const v = p.visor
  if (!v || v.tipo === 'Nenhum') return <span className="text-slate-500">sem visor</span>
  if (v.tipo === 'Poste') return <span>visor de poste em {v.porta} ({PROTOCOLOS.find((x) => x.valor === v.protocolo)?.nome}, {v.velocidade} bps)</span>
  return <span>segundo monitor{v.ecra ? ` (${nomeEcra(v.ecra)})` : ' (automático)'}</span>
}

/** Visor de poste ou segundo monitor do posto; portas COM e ecrãs vêm do agente do computador (detectados automaticamente). */
export function ConfigurarVisor({ posto, aoFechar }: { posto: PostoEstado; aoFechar: (gravado?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const actual: VisorPosto = posto.visor ?? { tipo: 'Nenhum', porta: null, protocolo: 'EscPos', velocidade: 9600, colunas: 20, ecra: null, endereco: null, portasDetectadas: [], ecrasDetectados: [] }
  const portas = actual.portasDetectadas
  const ecras = actual.ecrasDetectados
  const secundarios = ecras.filter((e) => !e.principal)
  // Automático: com o agente a ver só uma porta COM, fica escolhida; com um segundo monitor ligado, o tipo sugerido é o monitor.
  const sugerido: TipoVisor = actual.tipo !== 'Nenhum' ? actual.tipo : secundarios.length > 0 ? 'Monitor' : portas.length > 0 ? 'Poste' : 'Nenhum'
  const [tipo, setTipo] = useState<TipoVisor>(sugerido)
  const [gravou, setGravou] = useState(false)
  const [porta, setPorta] = useState(actual.porta ?? (portas.length === 1 ? portas[0].nome : ''))
  const [protocolo, setProtocolo] = useState<ProtocoloVisor>(actual.protocolo)
  const [velocidade, setVelocidade] = useState(actual.velocidade)
  const [colunas, setColunas] = useState(actual.colunas)
  const [ecra, setEcra] = useState(actual.ecra ?? '')
  const [endereco, setEndereco] = useState(actual.endereco)
  const [aGuardar, setAGuardar] = useState(false)
  const portaValida = /^COM\d{1,3}$/i.test(porta.trim())
  const alterado = tipo !== actual.tipo || (tipo === 'Poste' && (porta.trim().toUpperCase() !== actual.porta || protocolo !== actual.protocolo || velocidade !== actual.velocidade || colunas !== actual.colunas))
    || (tipo === 'Monitor' && (ecra || null) !== actual.ecra)

  const guardar = async (testar: boolean) => {
    setAGuardar(true)
    try {
      if (alterado || !testar) {
        await api.put(`/api/configuracoes/postos/${posto.id}/visor`, { tipo, porta: porta.trim() || null, protocolo, velocidade, colunas, ecra: ecra || null })
        setGravou(true)
      }
      if (testar) {
        await api.post(`/api/configuracoes/postos/${posto.id}/visor/testar`)
        avisos.sucesso(`Mensagem de teste enviada para o visor do posto ${posto.nome}.`)
        if (alterado) setEndereco((await api.get<PostoEstado[]>('/api/configuracoes/postos')).find((p) => p.id === posto.id)?.visor?.endereco ?? null)
        return
      }
      avisos.sucesso(tipo === 'Nenhum' ? 'Visor do cliente desligado.' : 'Visor do cliente guardado. O agente do posto liga-o em poucos segundos.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const novoEndereco = async () => {
    if (!window.confirm('Gerar um endereço novo para a página do visor? Os ecrãs e tablets com o endereço antigo deixam de mostrar o visor (o agente do posto passa logo para o novo).')) return
    try {
      await api.post(`/api/configuracoes/postos/${posto.id}/visor/endereco`)
      setEndereco((await api.get<PostoEstado[]>('/api/configuracoes/postos')).find((p) => p.id === posto.id)?.visor?.endereco ?? null)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const completo = endereco ? `${window.location.origin}${endereco}` : null
  const podeGuardar = podeGravar && !aGuardar && (tipo !== 'Poste' || portaValida)

  return (
    <Modal titulo={`Visor do cliente — ${posto.nome}`} aoFechar={() => aoFechar(gravou)} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar(gravou)}>Fechar</button>
        {tipo !== 'Nenhum' && (
          <button type="button" className="btn-secundario" disabled={!podeGuardar} onClick={() => void guardar(true)}>
            <MonitorSmartphone className="h-4 w-4" aria-hidden /> {alterado ? 'Guardar e testar' : 'Testar visor'}
          </button>
        )}
        <button type="button" className="btn-primario" disabled={!podeGuardar} onClick={() => void guardar(false)}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="space-y-4">
        {!posto.agenteRegistado ? (
          <Alerta tipo="aviso">
            Este posto não tem o agente do LAMGest instalado. O visor de poste e a abertura automática no segundo monitor precisam do agente
            (LAMGest-Setup, modo Posto, «Agente de impressão»). Sem ele, a página do visor pode ser aberta à mão num tablet ou noutro ecrã.
          </Alerta>
        ) : !posto.agenteLigado && (
          <Alerta tipo="aviso">O agente deste posto está desligado: as portas e ecrãs mostrados são os da última vez que ligou.</Alerta>
        )}

        {actual.tipo === 'Nenhum' && sugerido !== 'Nenhum' && (
          <Alerta tipo="info">
            Detectado automaticamente neste posto: {sugerido === 'Monitor' ? `segundo monitor (${secundarios.map((e) => nomeEcra(e.nome)).join(', ')})` : `porta ${portas.map((p) => p.nome).join(', ')}`}.
            Confirme e carregue em «Guardar e testar».
          </Alerta>
        )}

        <fieldset>
          <legend className="rotulo">Tipo de visor</legend>
          <div className="grid gap-2 sm:grid-cols-3">
            {([['Nenhum', 'Sem visor', 'O cliente não vê o pedido.'],
              ['Poste', 'Visor de poste', 'Visor de 2 linhas ligado por USB ou série (porta COM).'],
              ['Monitor', 'Segundo monitor', 'Monitor ou ecrã virado para o cliente, com artigos, total e troco.']] as const).map(([valor, nome, texto]) => (
              <label key={valor} className={`cursor-pointer rounded-lg border p-3 text-sm ${tipo === valor ? 'border-primaria-500 bg-primaria-50 ring-1 ring-primaria-500' : 'border-slate-200'}`}>
                <input type="radio" name="tipoVisor" className="mr-2" checked={tipo === valor} onChange={() => setTipo(valor)} />
                <span className="font-medium">{nome}</span>
                <span className="mt-1 block text-slate-600">{texto}</span>
              </label>
            ))}
          </div>
        </fieldset>

        {tipo === 'Poste' && (
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <label htmlFor="visorPorta" className="rotulo">Porta COM</label>
              {portas.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-2" aria-label="Portas detectadas">
                  {portas.map((p) => (
                    <button key={p.nome} type="button" onClick={() => setPorta(p.nome)}
                      className={`rounded-lg border px-3 py-2 text-left text-sm ${porta.toUpperCase() === p.nome ? 'border-primaria-500 bg-primaria-50' : 'border-slate-200'}`}>
                      <span className="font-mono font-semibold">{p.nome}</span>
                      {p.descricao && <span className="block text-xs text-slate-600">{p.descricao}</span>}
                    </button>
                  ))}
                </div>
              )}
              <input id="visorPorta" className="campo font-mono" maxLength={6} placeholder="COM3" value={porta} onChange={(e) => setPorta(e.target.value.toUpperCase())} />
              <p className="mt-1 text-xs text-slate-500">
                {portas.length > 0 ? 'Portas COM detectadas pelo agente neste computador. Ligue o visor e escolha a dele (o nome do aparelho ajuda).'
                  : posto.agenteRegistado ? 'O agente não encontrou portas COM: ligue o visor (e instale o controlador USB, se for preciso) e espere 1 minuto.'
                    : 'Escreva a porta (veja no Gestor de Dispositivos do Windows, em «Portas (COM e LPT)»).'}
              </p>
              {porta && !portaValida && <p className="mt-1 text-xs text-red-700">Porta no formato COM3.</p>}
            </div>
            <div>
              <label htmlFor="visorProtocolo" className="rotulo">Comandos do visor</label>
              <select id="visorProtocolo" className="campo" value={protocolo} onChange={(e) => setProtocolo(e.target.value as ProtocoloVisor)}>
                {PROTOCOLOS.map((p) => <option key={p.valor} value={p.valor}>{p.nome}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="visorVelocidade" className="rotulo">Velocidade (bps)</label>
              <select id="visorVelocidade" className="campo" value={velocidade} onChange={(e) => setVelocidade(Number(e.target.value))}>
                {VELOCIDADES.map((v) => <option key={v} value={v}>{v}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="visorColunas" className="rotulo">Caracteres por linha</label>
              <select id="visorColunas" className="campo" value={colunas} onChange={(e) => setColunas(Number(e.target.value))}>
                {COLUNAS.map((v) => <option key={v} value={v}>{v}</option>)}
              </select>
            </div>
            <p className="self-end text-xs text-slate-500">
              A maioria dos visores usa ESC/POS a 9600 bps com 20 caracteres. Se o teste mostrar símbolos estranhos, experimente CD5220 ou outra velocidade.
            </p>
          </div>
        )}

        {tipo === 'Monitor' && (
          <div className="space-y-3">
            <div>
              <label htmlFor="visorEcra" className="rotulo">Ecrã do cliente</label>
              <select id="visorEcra" className="campo" value={ecra} onChange={(e) => setEcra(e.target.value)}>
                <option value="">Automático (o primeiro monitor que não é o principal)</option>
                {ecras.map((e) => (
                  <option key={e.nome} value={e.nome} disabled={e.principal}>
                    {nomeEcra(e.nome)} — {e.largura}×{e.altura}{e.principal ? ' (principal, do operador)' : ''}
                  </option>
                ))}
              </select>
              {posto.agenteRegistado && ecras.length > 0 && secundarios.length === 0 && (
                <p className="mt-1 text-xs text-amber-700">O agente só vê um monitor neste computador. Ligue o segundo monitor (em «Alargar estes ecrãs» no Windows): o visor abre sozinho.</p>
              )}
              <p className="mt-1 text-xs text-slate-500">O agente do posto abre o visor em ecrã inteiro nesse monitor (Microsoft Edge, sem barras) e volta a abri-lo se o fecharem.</p>
            </div>
            {completo && (
              <div>
                <p className="rotulo">Endereço da página do visor</p>
                <div className="flex flex-wrap items-center gap-2">
                  <code className="min-w-0 flex-1 break-all rounded bg-slate-100 px-2 py-1 text-xs" aria-label="Endereço do visor">{completo}</code>
                  <button type="button" className="btn-secundario" onClick={() => void navigator.clipboard?.writeText(completo).then(() => avisos.sucesso('Endereço copiado.'))}>
                    <Copy className="h-4 w-4" aria-hidden /> Copiar
                  </button>
                  <a className="btn-secundario" href={completo} target="_blank" rel="noreferrer"><ExternalLink className="h-4 w-4" aria-hidden /> Abrir</a>
                  <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => void novoEndereco()}>
                    <RefreshCw className="h-4 w-4" aria-hidden /> Novo endereço
                  </button>
                </div>
                <p className="mt-1 text-xs text-slate-500">Também serve num tablet virado para o cliente (na mesma rede, com o endereço do servidor em vez de «localhost»).</p>
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}

/** Mensagens do visor (iguais em todos os postos). */
export function MensagensVisor() {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const config = useDados(() => api.get<{ chave: string; valor: string }[]>('/api/configuracoes'), [])
  const valor = (chave: string) => config.dados?.find((c) => c.chave === chave)?.valor ?? ''
  const [boasVindas, setBoasVindas] = useState<string | null>(null)
  const [agradecimento, setAgradecimento] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const bv = boasVindas ?? valor('visor.boas-vindas')
  const ag = agradecimento ?? valor('visor.agradecimento')
  const alterado = bv !== valor('visor.boas-vindas') || ag !== valor('visor.agradecimento')

  const guardar = async () => {
    setAGuardar(true)
    try {
      config.setDados(await api.put('/api/configuracoes', { valores: { 'visor.boas-vindas': bv.trim(), 'visor.agradecimento': ag.trim() } }))
      setBoasVindas(null)
      setAgradecimento(null)
      avisos.sucesso('Mensagens do visor guardadas.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <div className="cartao space-y-3 p-4" aria-label="Mensagens do visor do cliente">
      <h3 className="font-semibold">Mensagens do visor do cliente</h3>
      <p className="text-sm text-slate-600">Iguais em todos os postos. O visor de poste mostra só os primeiros caracteres (sem acentos).</p>
      <div className="grid gap-3 sm:grid-cols-2">
        <div>
          <label htmlFor="visorBoasVindas" className="rotulo">Sem venda</label>
          <input id="visorBoasVindas" className="campo" maxLength={100} value={bv} onChange={(e) => setBoasVindas(e.target.value)} placeholder="Bem-vindo!" />
        </div>
        <div>
          <label htmlFor="visorAgradecimento" className="rotulo">Depois de pago</label>
          <input id="visorAgradecimento" className="campo" maxLength={100} value={ag} onChange={(e) => setAgradecimento(e.target.value)} placeholder="Obrigado, volte sempre!" />
        </div>
      </div>
      <button type="button" className="btn-primario" disabled={!podeGravar || aGuardar || !alterado} onClick={() => void guardar()}>
        {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar mensagens
      </button>
    </div>
  )
}
