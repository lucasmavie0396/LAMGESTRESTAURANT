import { useState } from 'react'
import { Loader2, Pencil, Power } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Terminal } from '../../types/api'

/** Registo dos postos de caixa (permissão configuracoes.gerir). */
export function GestaoTerminais({ aoFechar }: { aoFechar: () => void }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const { dados, erro, recarregar } = useDados(() => api.get<Terminal[]>('/api/terminais?incluirInactivos=true'), [])
  const [nome, setNome] = useState('')
  const [descricao, setDescricao] = useState('')
  const [editarId, setEditarId] = useState<number | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const limpar = () => {
    setNome('')
    setDescricao('')
    setEditarId(null)
  }

  const guardar = async () => {
    setAGuardar(true)
    try {
      const corpo = { nome: nome.trim(), descricao: descricao.trim() || null }
      if (editarId) await api.put(`/api/terminais/${editarId}`, corpo)
      else await api.post('/api/terminais', corpo)
      avisos.sucesso(editarId ? 'Terminal actualizado.' : 'Terminal criado.')
      limpar()
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const alternar = async (t: Terminal) => {
    try {
      await api.post(`/api/terminais/${t.id}/activo`, { activo: !t.activo })
      avisos.sucesso(t.activo ? 'Terminal desactivado.' : 'Terminal activado.')
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <Modal titulo="Terminais de caixa" aoFechar={aoFechar} largura="max-w-2xl" rodape={<button type="button" className="btn-primario" onClick={aoFechar}>Fechar</button>}>
      <div className="space-y-4">
        <p className="text-sm text-slate-600">Registe cada computador que funciona como caixa. Em cada computador, escolha depois o terminal correspondente no ecrã Caixa.</p>
        <div className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
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
            <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={!podeGravar || !nome.trim() || aGuardar}>
              {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} {editarId ? 'Guardar' : 'Adicionar'}
            </button>
          </div>
        </div>
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        {!dados && !erro && <Carregando />}
        {dados && (
          <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
            {dados.length === 0 && <li className="p-3 text-sm text-slate-500">Ainda não há terminais.</li>}
            {dados.map((t) => (
              <li key={t.id} className="flex items-center gap-3 px-3 py-2">
                <div className="flex-1">
                  <p className="font-medium">{t.nome}</p>
                  <p className="text-xs text-slate-500">{t.descricao ?? 'Sem descrição'}{t.sessaoAbertaPor && ` · caixa aberto por ${t.sessaoAbertaPor}`}</p>
                </div>
                {!t.activo && <Etiqueta cor="cinza">Inactivo</Etiqueta>}
                <button type="button" className="btn-icone" aria-label={`Editar ${t.nome}`} disabled={!podeGravar}
                  onClick={() => { setEditarId(t.id); setNome(t.nome); setDescricao(t.descricao ?? '') }}>
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
