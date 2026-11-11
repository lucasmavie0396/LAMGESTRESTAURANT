import { useEffect, useState } from 'react'
import { Loader2, Monitor, Pencil, Plus, Power, Printer, RefreshCw, Tablet, Unplug, Wallet } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { CodigoTablets, PostoEstado, RedeServidor } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'
import { enderecoPreferido } from '../../utils/pda'
import { lerTerminalLocal } from '../../utils/terminalLocal'

/** Há quanto tempo (texto curto). */
function haQuanto(isoUtc: string) {
  const minutos = Math.max(0, Math.round((Date.now() - new Date(isoUtc).getTime()) / 60_000))
  if (minutos < 1) return 'agora'
  if (minutos < 60) return `há ${minutos} min`
  const horas = Math.round(minutos / 60)
  return horas < 48 ? `há ${horas} h` : formatarDataHora(isoUtc)
}

/**
 * Configurações → Postos: todos os postos registados, quais estão online (o sistema aberto no posto envia um sinal a cada minuto),
 * o caixa e a impressão de cada um, e o posto escolhido para a ligação dos tablets (app Android) pela rede.
 */
export function Postos() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const postos = useDados(() => api.get<PostoEstado[]>('/api/configuracoes/postos'), [])
  const [editar, setEditar] = useState<PostoEstado | 'novo' | null>(null)
  const [codigo, setCodigo] = useState<{ posto: PostoEstado; codigo: CodigoTablets } | null>(null)
  const esteComputador = lerTerminalLocal()

  // Actualiza o estado a cada 30 s (online/offline muda sem recarregar a página).
  const { recarregar } = postos
  useEffect(() => {
    const id = window.setInterval(() => void recarregar(), 30_000)
    return () => window.clearInterval(id)
  }, [recarregar])

  const executar = async (accao: () => Promise<unknown>, sucesso: string) => {
    try {
      await accao()
      avisos.sucesso(sucesso)
      await postos.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const ligarTablets = async (p: PostoEstado, novo: boolean) => {
    if (!novo && !window.confirm(`Escolher «${p.nome}» para a ligação dos tablets? Se outro posto estava escolhido, deixa de estar e os tablets terão de ser emparelhados de novo.`)) return
    if (novo && !window.confirm('Gerar um código novo? O código actual deixa de servir para emparelhar tablets.')) return
    try {
      const c = novo
        ? await api.post<CodigoTablets>(`/api/configuracoes/postos/${p.id}/tablets/codigo`)
        : await api.post<CodigoTablets>(`/api/configuracoes/postos/${p.id}/tablets`, { activo: true })
      setCodigo({ posto: p, codigo: c })
      await postos.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const lista = postos.dados ?? []
  const online = lista.filter((p) => p.online).length

  return (
    <section className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-lg font-semibold">Postos registados</h2>
          <p className="text-sm text-slate-600">
            {postos.dados ? `${online} de ${lista.filter((p) => p.activo).length} postos activos online.` : 'A carregar…'} Um posto está online quando o
            sistema está aberto nele (sinal a cada minuto; desliga ao fim de 2 minutos sem sinal).
          </p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secundario" onClick={() => void postos.recarregar()}>
            <RefreshCw className={`h-4 w-4 ${postos.aCarregar ? 'animate-spin' : ''}`} aria-hidden /> Actualizar
          </button>
          <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setEditar('novo')}>
            <Plus className="h-4 w-4" aria-hidden /> Novo posto
          </button>
        </div>
      </div>

      {postos.erro && <Alerta tipo="erro">{postos.erro}</Alerta>}
      {!postos.dados && !postos.erro && <Carregando />}
      {postos.dados?.length === 0 && <p className="text-sm text-slate-500">Nenhum posto registado.</p>}

      <ul className="grid gap-3 lg:grid-cols-2">
        {lista.map((p) => (
          <li key={p.id} className={`cartao space-y-3 p-4 ${p.activo ? '' : 'opacity-70'}`} aria-label={`Posto ${p.nome}`}>
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="flex items-center gap-3">
                <Monitor className={`h-8 w-8 ${p.online ? 'text-green-600' : 'text-slate-400'}`} aria-hidden />
                <div>
                  <p className="font-semibold">{p.nome}{p.numero !== null && <span className="text-slate-500"> · posto n.º {p.numero}</span>}</p>
                  {p.descricao && <p className="text-sm text-slate-600">{p.descricao}</p>}
                  {esteComputador === p.id && <p className="text-xs font-medium text-primaria-700">Este computador</p>}
                </div>
              </div>
              <div className="flex flex-wrap gap-1">
                {!p.activo ? <Etiqueta cor="cinza">Desactivado</Etiqueta>
                  : p.online ? <Etiqueta cor="verde">Online</Etiqueta>
                    : <Etiqueta cor="vermelho">Offline</Etiqueta>}
                {p.ligacaoTablets && <Etiqueta cor="azul"><Tablet className="mr-1 inline h-3.5 w-3.5" aria-hidden />Tablets</Etiqueta>}
              </div>
            </div>

            <dl className="grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
              <dt className="text-slate-500">Último sinal</dt>
              <dd>{p.vistoEmUtc ? `${haQuanto(p.vistoEmUtc)}${p.vistoPor ? ` · ${p.vistoPor}` : ''}${p.vistoEndereco ? ` · ${p.vistoEndereco}` : ''}` : 'nunca (o sistema ainda não foi aberto neste posto)'}</dd>
              <dt className="text-slate-500"><Wallet className="mr-1 inline h-3.5 w-3.5" aria-hidden />Caixa</dt>
              <dd>{p.caixasAbertos.length ? `aberto por ${p.caixasAbertos.join(', ')}` : 'fechado'}</dd>
              <dt className="text-slate-500"><Printer className="mr-1 inline h-3.5 w-3.5" aria-hidden />Impressão</dt>
              <dd>
                {!p.agenteRegistado ? 'sem agente de impressão (imprime pelo servidor ou pelo navegador)'
                  : p.agenteLigado ? <span className="text-green-700">agente ligado{p.agenteVersao ? ` (${p.agenteVersao})` : ''} · {p.impressorasWindows.length} impressoras no Windows</span>
                    : <span className="text-amber-700">agente desligado{p.agenteVistoEmUtc ? ` ${haQuanto(p.agenteVistoEmUtc)}` : ''}</span>}
                {p.impressorasConfiguradas.length > 0 && <span className="block text-slate-600">Configuradas: {p.impressorasConfiguradas.join(', ')}</span>}
              </dd>
            </dl>

            <div className="flex flex-wrap gap-2 border-t border-slate-100 pt-3">
              <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setEditar(p)}>
                <Pencil className="h-4 w-4" aria-hidden /> Editar
              </button>
              <button type="button" className="btn-secundario" disabled={!podeGravar}
                onClick={() => void executar(() => api.post(`/api/terminais/${p.id}/activo`, { activo: !p.activo }), p.activo ? 'Posto desactivado.' : 'Posto activado.')}>
                <Power className="h-4 w-4" aria-hidden /> {p.activo ? 'Desactivar' : 'Activar'}
              </button>
              {p.agenteRegistado && (
                <button type="button" className="btn-secundario" disabled={!podeGravar}
                  onClick={() => window.confirm(`Retirar a chave do agente de impressão do posto ${p.nome}? As impressoras deste posto deixam de imprimir até voltar a instalar o agente.`)
                    && void executar(() => api.delete(`/api/terminais/${p.id}/agente`), 'Agente de impressão desligado do posto.')}>
                  <Unplug className="h-4 w-4" aria-hidden /> Desligar agente
                </button>
              )}
              {p.activo && !p.ligacaoTablets && (
                <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => void ligarTablets(p, false)}>
                  <Tablet className="h-4 w-4" aria-hidden /> Ligar tablets a este posto
                </button>
              )}
              {p.ligacaoTablets && (
                <>
                  <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => void ligarTablets(p, true)}>
                    <Tablet className="h-4 w-4" aria-hidden /> Novo código dos tablets
                  </button>
                  <button type="button" className="btn-secundario" disabled={!podeGravar}
                    onClick={() => window.confirm(`Retirar a ligação dos tablets do posto ${p.nome}? O código deixa de servir.`)
                      && void executar(() => api.post(`/api/configuracoes/postos/${p.id}/tablets`, { activo: false }), 'Ligação dos tablets retirada.')}>
                    Retirar tablets
                  </button>
                </>
              )}
            </div>
            {p.ligacaoTablets && p.tabletsCodigoGeradoEmUtc && (
              <p className="text-xs text-slate-500">Código de emparelhamento gerado em {formatarDataHora(p.tabletsCodigoGeradoEmUtc)} (só se mostra quando é gerado).</p>
            )}
          </li>
        ))}
      </ul>

      <Alerta tipo="info" titulo="Ligação dos tablets (Android)">
        Escolha o posto a que os tablets ficam ligados: os pedidos feitos nos tablets vão usar o caixa e as impressoras desse posto. O sistema gera um código de
        emparelhamento e mostra o endereço do servidor na rede; a app dos tablets (ainda não disponível) vai lê-los pelo QR code.
      </Alerta>

      {editar && <EditarPosto posto={editar === 'novo' ? null : editar} aoFechar={(gravado) => { setEditar(null); if (gravado) void postos.recarregar() }} />}
      {codigo && <CodigoTabletsModal posto={codigo.posto} codigo={codigo.codigo} aoFechar={() => setCodigo(null)} />}
    </section>
  )
}

function EditarPosto({ posto, aoFechar }: { posto: PostoEstado | null; aoFechar: (gravado?: boolean) => void }) {
  const avisos = useAvisos()
  const [nome, setNome] = useState(posto?.nome ?? '')
  const [descricao, setDescricao] = useState(posto?.descricao ?? '')
  const [numero, setNumero] = useState(posto?.numero?.toString() ?? '')
  const [aGuardar, setAGuardar] = useState(false)
  const numeroValido = numero === '' || (/^\d{1,3}$/.test(numero) && Number(numero) >= 1)

  const guardar = async () => {
    setAGuardar(true)
    try {
      const corpo = { nome: nome.trim(), descricao: descricao.trim() || null, numero: numero === '' ? null : Number(numero) }
      if (posto) await api.put(`/api/terminais/${posto.id}`, corpo)
      else await api.post('/api/terminais', corpo)
      avisos.sucesso(posto ? 'Posto actualizado.' : 'Posto criado.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={posto ? `Editar ${posto.nome}` : 'Novo posto'} aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aGuardar || !nome.trim() || !numeroValido} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="space-y-3">
        <div><label htmlFor="postoNome" className="rotulo">Nome</label><input id="postoNome" className="campo" maxLength={200} value={nome} onChange={(e) => setNome(e.target.value)} /></div>
        <div>
          <label htmlFor="postoNumero" className="rotulo">Número do posto (opcional)</label>
          <input id="postoNumero" className="campo" inputMode="numeric" maxLength={3} value={numero} onChange={(e) => setNumero(e.target.value)} />
          <p className="mt-1 text-xs text-slate-500">O número indicado no instalador do computador (atalho com ?posto=N).</p>
          {!numeroValido && <p className="mt-1 text-xs text-red-700">Número de 1 a 999.</p>}
        </div>
        <div><label htmlFor="postoDescricao" className="rotulo">Descrição (opcional)</label><input id="postoDescricao" className="campo" maxLength={1000} value={descricao} onChange={(e) => setDescricao(e.target.value)} /></div>
      </div>
    </Modal>
  )
}

/** Código de emparelhamento (mostrado só agora), endereço do servidor na rede e QR code para a app dos tablets. */
function CodigoTabletsModal({ posto, codigo, aoFechar }: { posto: PostoEstado; codigo: CodigoTablets; aoFechar: () => void }) {
  const rede = useDados(() => api.get<RedeServidor>('/api/configuracoes/postos/rede'), [])
  const [endereco, setEndereco] = useState<string | null>(null)
  // Nunca «localhost»: o tablet só chega ao servidor pelo endereço da rede local, escolhido pelo IP do posto.
  const escolhido = endereco ?? enderecoPreferido(rede.dados?.enderecos ?? [], posto.vistoEndereco)
  const semRede = !!rede.dados && !escolhido
  const texto = `lamgest://emparelhar?servidor=${encodeURIComponent(escolhido ?? '')}&posto=${posto.numero ?? posto.id}&codigo=${codigo.codigo}`
  const qr = useDados(() => (escolhido ? api.post<{ svg: string }>('/api/configuracoes/postos/qr', { texto }) : Promise.resolve(null)), [texto])

  return (
    <Modal titulo={`Tablets ligados a ${posto.nome}`} aoFechar={aoFechar} largura="max-w-xl"
      rodape={<button type="button" className="btn-primario" onClick={aoFechar}>Fechar</button>}>
      <div className="space-y-4">
        <Alerta tipo="aviso">Anote o código: só é mostrado agora. Se o perder, gere um código novo (o antigo deixa de servir).</Alerta>
        <div className="text-center">
          <p className="text-sm text-slate-600">Código de emparelhamento</p>
          <p className="font-mono text-3xl font-bold tracking-widest" aria-label="Código de emparelhamento">{codigo.codigo}</p>
        </div>
        <div>
          <label htmlFor="tabletsServidor" className="rotulo">Endereço do servidor na rede</label>
          {rede.dados && rede.dados.enderecos.length > 1 ? (
            <select id="tabletsServidor" className="campo" value={escolhido ?? ''} onChange={(e) => setEndereco(e.target.value)}>
              {rede.dados.enderecos.map((e) => <option key={e} value={e}>{e}</option>)}
            </select>
          ) : <p id="tabletsServidor" className="font-mono text-sm">{escolhido}</p>}
          {semRede && <Alerta tipo="erro">O servidor não tem nenhuma placa ligada à rede local. Ligue-o ao router ou switch do estabelecimento.</Alerta>}
          <p className="mt-1 text-xs text-slate-500">
            Os tablets têm de estar na mesma rede (Wi-Fi) do posto{posto.vistoEndereco ? ` (IP ${posto.vistoEndereco})` : ''}. Não é preciso internet.
          </p>
        </div>
        <div className="flex justify-center">
          {qr.dados
            ? <img src={`data:image/svg+xml;charset=utf-8,${encodeURIComponent(qr.dados.svg)}`} alt="QR code de emparelhamento dos tablets" className="h-56 w-56" />
            : qr.erro ? <Alerta tipo="erro">{qr.erro}</Alerta> : !semRede && <Carregando />}
        </div>
      </div>
    </Modal>
  )
}
