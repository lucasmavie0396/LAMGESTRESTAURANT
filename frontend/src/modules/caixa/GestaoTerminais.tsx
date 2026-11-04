import { useState } from 'react'
import { Loader2, Pencil, Power, Printer, PrinterCheck, Unplug } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Terminal } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'

/** Igual ao servidor (ServicoAgentes.Ligado): sem notícias do agente há mais de 3 minutos, o posto aparece desligado. */
const LIGADO_MS = 3 * 60_000

function EstadoAgente({ t }: { t: Terminal }) {
  if (!t.agenteRegistado) return null
  const ligado = !!t.agenteVistoEmUtc && Date.now() - new Date(t.agenteVistoEmUtc).getTime() <= LIGADO_MS
  const impressoras = t.agenteImpressoras ?? []
  const dica = (impressoras.length ? 'Impressoras: ' + impressoras.join(', ') : 'Sem impressoras') + (t.agenteVersao ? ` · agente ${t.agenteVersao}` : '')
  return ligado
    ? <span title={dica}><Etiqueta cor="verde"><PrinterCheck className="mr-1 inline h-3.5 w-3.5" aria-hidden />Impressão ligada · {impressoras.length}</Etiqueta></span>
    : <span title={dica}><Etiqueta cor="amarelo"><Printer className="mr-1 inline h-3.5 w-3.5" aria-hidden />Agente desligado{t.agenteVistoEmUtc ? ` desde ${formatarDataHora(t.agenteVistoEmUtc)}` : ''}</Etiqueta></span>
}

/** Registo dos postos de trabalho/caixa (permissão configuracoes.gerir). */
export function GestaoTerminais({ aoFechar }: { aoFechar: () => void }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const { dados, erro, recarregar } = useDados(() => api.get<Terminal[]>('/api/terminais?incluirInactivos=true'), [])
  const [nome, setNome] = useState('')
  const [descricao, setDescricao] = useState('')
  const [numero, setNumero] = useState('')
  const [editarId, setEditarId] = useState<number | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const numeroValido = numero === '' || (/^\d{1,3}$/.test(numero) && Number(numero) >= 1)

  const limpar = () => {
    setNome('')
    setDescricao('')
    setNumero('')
    setEditarId(null)
  }

  const guardar = async () => {
    setAGuardar(true)
    try {
      const corpo = { nome: nome.trim(), descricao: descricao.trim() || null, numero: numero === '' ? null : Number(numero) }
      if (editarId) await api.put(`/api/terminais/${editarId}`, corpo)
      else await api.post('/api/terminais', corpo)
      avisos.sucesso(editarId ? 'Posto actualizado.' : 'Posto criado.')
      limpar()
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const revogarAgente = async (t: Terminal) => {
    if (!window.confirm(`Retirar a chave do agente de impressão do posto ${t.nome}? As impressoras ligadas a este posto deixam de imprimir até voltar a instalar o agente.`)) return
    try {
      await api.delete(`/api/terminais/${t.id}/agente`)
      avisos.sucesso('Agente de impressão desligado do posto.')
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const alternar = async (t: Terminal) => {
    try {
      await api.post(`/api/terminais/${t.id}/activo`, { activo: !t.activo })
      avisos.sucesso(t.activo ? 'Posto desactivado.' : 'Posto activado.')
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <Modal titulo="Postos (terminais)" aoFechar={aoFechar} largura="max-w-3xl" rodape={<button type="button" className="btn-primario" onClick={aoFechar}>Fechar</button>}>
      <div className="space-y-4">
        <p className="text-sm text-slate-600">
          Registe cada computador (caixa, cozinha, gerência…) com um número de posto. No instalador do computador indica-se esse número:
          o atalho criado já abre o sistema associado ao posto. Também pode escolher o posto manualmente no ecrã Caixa.
          Com o <strong>agente de impressão</strong> (opção do instalador), o posto imprime nas impressoras instaladas no seu computador.
        </p>
        <div className="grid gap-3 sm:grid-cols-[6rem_1fr_1fr_auto] sm:items-end">
          <div>
            <label htmlFor="numeroTerminal" className="rotulo">N.º posto</label>
            <input id="numeroTerminal" className="campo" inputMode="numeric" maxLength={3} value={numero}
              onChange={(e) => setNumero(e.target.value.trim())} placeholder="Ex.: 1" aria-invalid={!numeroValido} />
          </div>
          <div>
            <label htmlFor="nomeTerminal" className="rotulo">{editarId ? 'Nome (a editar)' : 'Nome'}</label>
            <input id="nomeTerminal" className="campo" maxLength={200} value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: Caixa 1 — Balcão" />
          </div>
          <div>
            <label htmlFor="descTerminal" className="rotulo">Descrição</label>
            <input id="descTerminal" className="campo" maxLength={1000} value={descricao} onChange={(e) => setDescricao(e.target.value)} />
          </div>
          <div className="flex gap-2">
            {editarId && <button type="button" className="btn-secundario" onClick={limpar}>Cancelar</button>}
            <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={!podeGravar || !nome.trim() || !numeroValido || aGuardar}>
              {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} {editarId ? 'Guardar' : 'Adicionar'}
            </button>
          </div>
        </div>
        {!numeroValido && <p className="text-sm text-red-700">O número do posto é um número de 1 a 999 (ou vazio).</p>}
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        {!dados && !erro && <Carregando />}
        {dados && (
          <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
            {dados.length === 0 && <li className="p-3 text-sm text-slate-500">Ainda não há postos.</li>}
            {dados.map((t) => (
              <li key={t.id} className="flex items-center gap-3 px-3 py-2">
                <span className="w-14 shrink-0 text-center text-sm font-semibold text-primaria-700">{t.numero ? `N.º ${t.numero}` : '—'}</span>
                <div className="flex-1">
                  <p className="font-medium">{t.nome}</p>
                  <p className="text-xs text-slate-500">{t.descricao ?? 'Sem descrição'}{t.sessaoAbertaPor && ` · caixa aberto por ${t.sessaoAbertaPor}`}</p>
                </div>
                <EstadoAgente t={t} />
                {!t.activo && <Etiqueta cor="cinza">Inactivo</Etiqueta>}
                {t.agenteRegistado && (
                  <button type="button" className="btn-icone" title="Desligar o agente de impressão" aria-label={`Desligar o agente de impressão de ${t.nome}`} disabled={!podeGravar}
                    onClick={() => void revogarAgente(t)}>
                    <Unplug className="h-4 w-4" />
                  </button>
                )}
                <button type="button" className="btn-icone" aria-label={`Editar ${t.nome}`} disabled={!podeGravar}
                  onClick={() => { setEditarId(t.id); setNome(t.nome); setDescricao(t.descricao ?? ''); setNumero(t.numero ? String(t.numero) : '') }}>
                  <Pencil className="h-4 w-4" />
                </button>
                <button type="button" className="btn-icone" aria-label={`${t.activo ? 'Desactivar' : 'Activar'} ${t.nome}`} disabled={!podeGravar} onClick={() => void alternar(t)}>
                  <Power className={`h-4 w-4 ${t.activo ? 'text-red-600' : 'text-green-700'}`} />
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  )
}
