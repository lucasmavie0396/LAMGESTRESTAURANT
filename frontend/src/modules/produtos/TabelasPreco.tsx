import { useState } from 'react'
import { Loader2, Pencil, Plus, Power } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { TabelaPreco } from '../../types/api'

/**
 * Produtos → Tabelas de preço: preços próprios por zona (Esplanada, VIP…) ou para o botão «Retalho» do balcão. O preço de
 * cada produto em cada tabela escreve-se na ficha do produto; sem preço na tabela, vende ao preço normal.
 */
export function TabelasPreco({ podeGerir }: { podeGerir: boolean }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const lista = useDados(() => api.get<TabelaPreco[]>('/api/tabelas-preco?incluirInactivas=true'), [])
  const [editar, setEditar] = useState<TabelaPreco | 'nova' | null>(null)

  const alternar = async (t: TabelaPreco) => {
    try {
      await api.post(`/api/tabelas-preco/${t.id}/activo`, { activo: !t.activo })
      avisos.sucesso(t.activo ? 'Tabela desactivada.' : 'Tabela activada.')
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <section className="cartao p-4">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-semibold">Tabelas de preço</h2>
          <p className="max-w-3xl text-sm text-slate-600">
            Preços diferentes para alguns produtos: associe a tabela a uma zona (Mesas → Gerir mesas e zonas) e as mesas dessa zona vendem com ela, ou
            escolha-a como «Retalho» do balcão (Configurações → Documentos e POS). O preço de cada produto escreve-se na ficha do produto; sem preço na
            tabela, vende ao preço normal.
          </p>
        </div>
        {podeGerir && (
          <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setEditar('nova')}>
            <Plus className="h-4 w-4" aria-hidden /> Nova tabela
          </button>
        )}
      </div>
      {lista.erro && <Alerta tipo="erro">{lista.erro}</Alerta>}
      {!lista.dados && !lista.erro && <Carregando />}
      {lista.dados?.length === 0 && <Vazio texto="Ainda não há tabelas de preço. Todos os produtos vendem ao preço normal." />}
      {!!lista.dados?.length && (
        <ul className="divide-y divide-slate-100">
          {lista.dados.map((t) => (
            <li key={t.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
              <div>
                <p className={`font-medium ${t.activo ? '' : 'text-slate-400'}`}>
                  {t.nome} {t.retalho && <Etiqueta cor="azul">Retalho do balcão</Etiqueta>} {!t.activo && <Etiqueta cor="cinza">Inactiva</Etiqueta>}
                </p>
                <p className="text-sm text-slate-600">
                  {t.produtos} produto(s) com preço próprio{t.zonas.length > 0 && ` · zonas: ${t.zonas.join(', ')}`}{t.descricao && ` · ${t.descricao}`}
                </p>
              </div>
              {podeGerir && (
                <div className="flex gap-2">
                  <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => setEditar(t)}><Pencil className="h-4 w-4" aria-hidden /> Editar</button>
                  <button type="button" className="btn-secundario" disabled={!podeGravar} onClick={() => void alternar(t)}>
                    <Power className="h-4 w-4" aria-hidden /> {t.activo ? 'Desactivar' : 'Activar'}
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {editar && <FormTabela tabela={editar === 'nova' ? null : editar} aoFechar={(ok) => { setEditar(null); if (ok) void lista.recarregar() }} />}
    </section>
  )
}

function FormTabela({ tabela, aoFechar }: { tabela: TabelaPreco | null; aoFechar: (ok?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [nome, setNome] = useState(tabela?.nome ?? '')
  const [descricao, setDescricao] = useState(tabela?.descricao ?? '')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const guardar = async () => {
    if (!nome.trim()) return setErro('Indique o nome da tabela.')
    setAGuardar(true)
    setErro(null)
    try {
      const corpo = { nome: nome.trim(), descricao: descricao.trim() || null }
      if (tabela) await api.put(`/api/tabelas-preco/${tabela.id}`, corpo)
      else await api.post('/api/tabelas-preco', corpo)
      avisos.sucesso(tabela ? 'Tabela actualizada.' : 'Tabela criada. Escreva os preços na ficha de cada produto.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={tabela ? `Tabela ${tabela.nome}` : 'Nova tabela de preço'} aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aGuardar || !podeGravar} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="space-y-3">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <div><label htmlFor="tpNome" className="rotulo">Nome</label><input id="tpNome" className="campo" maxLength={100} autoFocus value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Esplanada, VIP, Retalho…" /></div>
        <div><label htmlFor="tpDesc" className="rotulo">Descrição (opcional)</label><input id="tpDesc" className="campo" maxLength={300} value={descricao} onChange={(e) => setDescricao(e.target.value)} /></div>
      </div>
    </Modal>
  )
}
