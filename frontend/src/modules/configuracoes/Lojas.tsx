import { useEffect, useState } from 'react'
import { Download, Loader2, Plus, Store, Trash2 } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Confirmacao } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Configuracao, ListaLojas, LojaLigada } from '../../types/api'

/**
 * Configurações → Lojas: outras lojas do mesmo dono (cada uma com o seu servidor), ligadas pela VPN, para o seletor de lojas
 * no ecrã de entrada e no topo. Ao acrescentar, o sistema contacta essa loja e confirma o nome e o NUIT.
 */
export function Lojas() {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const lista = useDados(() => api.get<ListaLojas>('/api/lojas'), [])
  const config = useDados(() => api.get<Configuracao[]>('/api/configuracoes'), [])
  const [endereco, setEndereco] = useState('')
  const [cor, setCor] = useState('')
  const [aProcessar, setAProcessar] = useState<'acrescentar' | 'importar' | 'cor' | null>(null)
  const [retirar, setRetirar] = useState<LojaLigada | null>(null)
  const corGuardada = config.dados?.find((c) => c.chave === 'loja.cor')?.valor ?? '#2563eb'

  useEffect(() => { if (config.dados) setCor(corGuardada) }, [config.dados, corGuardada])

  const executar = async (accao: 'acrescentar' | 'importar') => {
    setAProcessar(accao)
    try {
      if (accao === 'acrescentar') {
        const l = await api.post<LojaLigada>('/api/lojas', { endereco })
        avisos.sucesso(`Loja ${l.nome} ligada.`)
      } else {
        const r = await api.post<ListaLojas>('/api/lojas/importar', { endereco })
        avisos.sucesso(`Lista importada: ${r.outras.length} loja(s) ligada(s).`)
      }
      setEndereco('')
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAProcessar(null)
    }
  }

  const guardarCor = async () => {
    setAProcessar('cor')
    try {
      config.setDados(await api.put<Configuracao[]>('/api/configuracoes', { valores: { 'loja.cor': cor } }))
      avisos.sucesso('Cor guardada. As outras lojas vêem-na quando voltarem a importar a lista.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAProcessar(null)
    }
  }

  const confirmarRetirar = async () => {
    if (!retirar) return
    try {
      await api.delete(`/api/lojas/${retirar.id}`)
      avisos.sucesso(`Loja ${retirar.nome} retirada da lista.`)
      setRetirar(null)
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  if (lista.erro) return <Alerta tipo="erro">{lista.erro}</Alerta>
  if (!lista.dados) return <Carregando />
  const d = lista.dados

  return (
    <section className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold">Lojas ligadas</h2>
        <p className="text-sm text-slate-600">
          Outras lojas do mesmo dono, cada uma com o seu servidor, ligadas pela VPN. Aparecem no seletor «Loja» do ecrã de entrada e no topo
          (para quem tem a permissão «Mudar de loja»). As vendas, o stock e os documentos ficam sempre no servidor de cada loja.
        </p>
      </div>

      <div className="cartao flex flex-wrap items-end gap-3 p-4">
        <div className="flex items-center gap-2">
          <Store className="h-5 w-5" style={{ color: cor || corGuardada }} aria-hidden />
          <span className="font-semibold">Esta loja: {d.actual.nome}</span>
        </div>
        <label className="text-sm">Cor desta loja
          <input type="color" className="ml-2 h-9 w-14 cursor-pointer rounded border border-slate-300 align-middle" value={cor || corGuardada} onChange={(e) => setCor(e.target.value)} />
        </label>
        <button type="button" className="btn-secundario" disabled={!podeGravar || aProcessar !== null || cor === corGuardada} onClick={() => void guardarCor()}>Guardar cor</button>
      </div>

      <div className="cartao space-y-3 p-4">
        <div>
          <label htmlFor="enderecoLoja" className="rotulo">Endereço da outra loja na VPN</label>
          <input id="enderecoLoja" className="campo" placeholder="http://100.70.1.11:8080" maxLength={200} value={endereco} onChange={(e) => setEndereco(e.target.value)} />
          <p className="mt-1 text-xs text-slate-500">O endereço está em Configurações → Postos → «Tablets fora do restaurante» do servidor dessa loja.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" className="btn-primario" disabled={!podeGravar || !endereco.trim() || aProcessar !== null} onClick={() => void executar('acrescentar')}>
            {aProcessar === 'acrescentar' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Plus className="h-4 w-4" aria-hidden />} Acrescentar loja
          </button>
          <button type="button" className="btn-secundario" disabled={!podeGravar || !endereco.trim() || aProcessar !== null} onClick={() => void executar('importar')}
            title="Acrescenta essa loja e todas as que ela já conhece">
            {aProcessar === 'importar' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Download className="h-4 w-4" aria-hidden />} Importar a lista dessa loja
          </button>
        </div>
      </div>

      {d.outras.length === 0 ? (
        <p className="text-sm text-slate-500">Ainda não há outras lojas ligadas.</p>
      ) : (
        <ul className="cartao divide-y divide-slate-100">
          {d.outras.map((l) => (
            <li key={l.id} className="flex flex-wrap items-center gap-3 px-4 py-3" aria-label={`Loja ${l.nome}`}>
              <span className="h-3 w-3 rounded-full" style={{ backgroundColor: l.cor ?? '#2563eb' }} aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="font-medium">{l.nome}</p>
                <p className="text-xs text-slate-500">{l.endereco}{l.nuit ? ` · NUIT ${l.nuit}` : ''}</p>
              </div>
              <a className="btn-secundario" href={`${l.endereco}/login`}>Abrir</a>
              <button type="button" className="btn-icone text-red-700" aria-label={`Retirar ${l.nome}`} disabled={!podeGravar} onClick={() => setRetirar(l)}>
                <Trash2 className="h-4 w-4" />
              </button>
            </li>
          ))}
        </ul>
      )}

      {retirar && (
        <Confirmacao titulo="Retirar loja da lista" mensagem={<>A loja <strong>{retirar.nome}</strong> deixa de aparecer no seletor desta loja. Os dados dela não são afectados.</>}
          textoConfirmar="Retirar" perigo aoCancelar={() => setRetirar(null)} aoConfirmar={() => void confirmarRetirar()} />
      )}
    </section>
  )
}
