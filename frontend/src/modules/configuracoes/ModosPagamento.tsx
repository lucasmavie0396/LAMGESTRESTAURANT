import { useState } from 'react'
import { Check, Loader2, Pencil, Plus, Power } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { MetodoPagamento } from '../../types/api'

function Sim({ v }: { v: boolean }) {
  return v ? <Check className="mx-auto h-4 w-4 text-green-700" aria-label="Sim" /> : <span className="block text-center text-slate-300" aria-label="Não">—</span>
}

function Opcao({ marcado, aoMudar, titulo, ajuda }: { marcado: boolean; aoMudar: (v: boolean) => void; titulo: string; ajuda: string }) {
  return (
    <label className="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200 p-3 text-sm">
      <input type="checkbox" className="mt-0.5 h-4 w-4" checked={marcado} onChange={(e) => aoMudar(e.target.checked)} />
      <span><span className="font-medium">{titulo}</span><span className="block text-xs text-slate-500">{ajuda}</span></span>
    </label>
  )
}

/** Modos de pagamento: criam-se e editam-se; nunca se eliminam (desactivam-se, mantendo o histórico). */
export function ModosPagamento() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const metodos = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  const [editar, setEditar] = useState<MetodoPagamento | 'novo' | null>(null)

  const alternar = async (m: MetodoPagamento) => {
    try {
      await api.post(`/api/configuracoes/metodos-pagamento/${m.id}/activo`, { activo: !m.activo })
      avisos.sucesso(m.activo ? `${m.nome} desactivado: deixa de aparecer no POS.` : `${m.nome} activado.`)
      await metodos.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <section className="cartao p-4">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-semibold">Modos de pagamento</h2>
          <p className="max-w-3xl text-sm text-slate-600">
            Aparecem no POS pela ordem indicada. Um modo já usado não se elimina: desactive-o (os pagamentos antigos mantêm-se nos relatórios e nos fechos de caixa).
          </p>
        </div>
        <button type="button" className="btn-primario" disabled={!podeGravar} onClick={() => setEditar('novo')}>
          <Plus className="h-4 w-4" aria-hidden /> Novo modo de pagamento
        </button>
      </div>
      {metodos.erro && <Alerta tipo="erro">{metodos.erro}</Alerta>}
      {!metodos.dados && !metodos.erro && <Carregando />}
      {metodos.dados && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-slate-600">
              <tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Ordem</th>
                <th scope="col" className="py-2">Nome</th>
                <th scope="col" className="py-2">Código</th>
                <th scope="col" className="py-2 text-center">Dá troco</th>
                <th scope="col" className="py-2 text-center">Exige referência</th>
                <th scope="col" className="py-2 text-center">Conta na gaveta</th>
                <th scope="col" className="py-2 text-right">Acções</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {metodos.dados.map((m) => (
                <tr key={m.id} className={m.activo ? '' : 'opacity-60'}>
                  <td className="py-2 tabular-nums">{m.ordem}</td>
                  <td className="py-2 font-medium">{m.nome}{!m.activo && <span className="ml-2"><Etiqueta cor="cinza">Inactivo</Etiqueta></span>}</td>
                  <td className="py-2 font-mono text-xs text-slate-500">{m.codigo}</td>
                  <td className="py-2"><Sim v={m.permiteTroco} /></td>
                  <td className="py-2"><Sim v={m.exigeReferencia} /></td>
                  <td className="py-2"><Sim v={m.contaNaGaveta} /></td>
                  <td className="py-2">
                    <div className="flex justify-end gap-1">
                      <button type="button" className="btn-icone" title="Editar" aria-label={`Editar ${m.nome}`} disabled={!podeGravar} onClick={() => setEditar(m)}>
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button type="button" className="btn-icone" title={m.activo ? 'Desactivar' : 'Activar'} aria-label={`${m.activo ? 'Desactivar' : 'Activar'} ${m.nome}`}
                        disabled={!podeGravar} onClick={() => void alternar(m)}>
                        <Power className={`h-4 w-4 ${m.activo ? 'text-red-600' : 'text-green-700'}`} />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editar && (
        <FormModo metodo={editar === 'novo' ? null : editar} ordemSugerida={Math.max(0, ...(metodos.dados?.map((m) => m.ordem) ?? [0])) + 1}
          aoFechar={(gravou) => { setEditar(null); if (gravou) void metodos.recarregar() }} />
      )}
    </section>
  )
}

function FormModo({ metodo, ordemSugerida, aoFechar }: { metodo: MetodoPagamento | null; ordemSugerida: number; aoFechar: (gravou: boolean) => void }) {
  const avisos = useAvisos()
  const [nome, setNome] = useState(metodo?.nome ?? '')
  const [ordem, setOrdem] = useState(String(metodo?.ordem ?? ordemSugerida))
  const [permiteTroco, setPermiteTroco] = useState(metodo?.permiteTroco ?? false)
  const [exigeReferencia, setExigeReferencia] = useState(metodo?.exigeReferencia ?? false)
  const [contaNaGaveta, setContaNaGaveta] = useState(metodo?.contaNaGaveta ?? false)
  const [aGuardar, setAGuardar] = useState(false)
  const ordemValida = /^\d{1,3}$/.test(ordem)

  const guardar = async () => {
    setAGuardar(true)
    try {
      const corpo = { nome: nome.trim(), permiteTroco, exigeReferencia, contaNaGaveta, ordem: Number(ordem) }
      if (metodo) await api.put(`/api/configuracoes/metodos-pagamento/${metodo.id}`, corpo)
      else await api.post('/api/configuracoes/metodos-pagamento', corpo)
      avisos.sucesso(metodo ? 'Modo de pagamento actualizado.' : 'Modo de pagamento criado.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={metodo ? `Editar ${metodo.nome}` : 'Novo modo de pagamento'} aoFechar={() => aoFechar(false)}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar(false)}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aGuardar || !nome.trim() || !ordemValida} onClick={() => void guardar()}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-[1fr_7rem]">
          <div>
            <label htmlFor="modoNome" className="rotulo">Nome</label>
            <input id="modoNome" className="campo" maxLength={100} value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: Cartão VISA, POS BCI, Vale" />
          </div>
          <div>
            <label htmlFor="modoOrdem" className="rotulo">Ordem</label>
            <input id="modoOrdem" className="campo" inputMode="numeric" maxLength={3} value={ordem} onChange={(e) => setOrdem(e.target.value.trim())} aria-invalid={!ordemValida} />
          </div>
        </div>
        {metodo && <p className="text-xs text-slate-500">Código <span className="font-mono">{metodo.codigo}</span> (não muda, para manter o histórico).</p>}
        <div className="space-y-2">
          <Opcao marcado={contaNaGaveta} aoMudar={(v) => { setContaNaGaveta(v); if (!v) setPermiteTroco(false) }}
            titulo="Conta na gaveta" ajuda="Dinheiro físico: entra no valor esperado em dinheiro no fecho do caixa." />
          <Opcao marcado={permiteTroco} aoMudar={(v) => { setPermiteTroco(v); if (v) setContaNaGaveta(true) }}
            titulo="Dá troco" ajuda="O valor recebido pode ser superior ao valor a pagar (só para dinheiro na gaveta)." />
          <Opcao marcado={exigeReferencia} aoMudar={setExigeReferencia}
            titulo="Exige referência" ajuda="O operador tem de indicar o n.º da transacção (ex.: M-Pesa, transferência, talão do POS bancário)." />
        </div>
      </div>
    </Modal>
  )
}
