import { useState } from 'react'
import { Loader2, Pencil, Plus, Power } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { FuncaoFuncionario, Funcionario, Opcao } from '../../types/api'
import { dataCurta } from '../../utils/datas'

const NOME_FUNCAO: Record<FuncaoFuncionario, string> = {
  Garcom: 'Garçom', Cozinheiro: 'Cozinheiro', Caixa: 'Caixa', Estafeta: 'Estafeta', Barman: 'Barman', Gerente: 'Gerente', Outro: 'Outro',
}

export function PaginaFuncionarios() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [inactivos, setInactivos] = useState(false)
  const [editar, setEditar] = useState<Funcionario | 'novo' | null>(null)
  const lista = useDados(() => api.get<Funcionario[]>(`/api/funcionarios?incluirInactivos=${inactivos}`), [inactivos])
  const podeGerir = tem('funcionarios.gerir')

  const alternar = async (f: Funcionario) => {
    try {
      await api.post(`/api/funcionarios/${f.id}/activo`, { activo: !f.activo })
      avisos.sucesso(f.activo ? 'Funcionário desactivado.' : 'Funcionário activado.')
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="mx-auto max-w-6xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Funcionários</h1>
        {podeGerir && <button type="button" className="btn-primario" onClick={() => setEditar('novo')} disabled={!podeGravar}><Plus className="h-4 w-4" aria-hidden /> Novo funcionário</button>}
      </div>
      <p className="text-sm text-slate-600">
        Associe cada garçom ao seu utilizador: os pedidos que ele abrir no POS ficam atribuídos automaticamente (relatório «Por garçom»).
        {!tem('funcionarios.dados-pessoais') && ' Os documentos de identificação não são mostrados ao seu perfil.'}
      </p>
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4" checked={inactivos} onChange={(e) => setInactivos(e.target.checked)} /> Mostrar inactivos</label>
      <div className="cartao">
        {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
        {!lista.dados && !lista.erro && <Carregando />}
        {lista.dados?.length === 0 && <Vazio texto="Sem funcionários registados." />}
        {lista.dados && lista.dados.length > 0 && (
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-600">
              <tr>
                <th scope="col" className="px-4 py-3">Nome</th>
                <th scope="col" className="px-4 py-3">Função</th>
                <th scope="col" className="px-4 py-3">Contactos</th>
                <th scope="col" className="px-4 py-3">Utilizador</th>
                <th scope="col" className="px-4 py-3 text-right">Acções</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {lista.dados.map((f) => (
                <tr key={f.id} className={f.activo ? '' : 'opacity-60'}>
                  <td className="px-4 py-2">
                    <p className="font-medium">{f.nome}</p>
                    <p className="text-xs text-slate-500">{f.dataAdmissao && `Admitido em ${dataCurta(f.dataAdmissao)}`}{f.documentoIdentificacao && ` · ${f.documentoIdentificacao}`}</p>
                  </td>
                  <td className="px-4 py-2"><Etiqueta cor="azul">{NOME_FUNCAO[f.funcao]}</Etiqueta></td>
                  <td className="px-4 py-2 text-slate-600">{[f.telefone, f.email].filter(Boolean).join(' · ') || '—'}</td>
                  <td className="px-4 py-2 text-slate-600">{f.utilizador ?? '—'}</td>
                  <td className="px-4 py-2">
                    {podeGerir && (
                      <div className="flex justify-end">
                        <button type="button" className="btn-icone" aria-label={`Editar ${f.nome}`} disabled={!podeGravar} onClick={() => setEditar(f)}><Pencil className="h-4 w-4" /></button>
                        <button type="button" className="btn-icone" aria-label={`${f.activo ? 'Desactivar' : 'Activar'} ${f.nome}`} disabled={!podeGravar} onClick={() => void alternar(f)}>
                          <Power className={`h-4 w-4 ${f.activo ? 'text-red-600' : 'text-green-700'}`} />
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {editar && <FormFuncionario funcionario={editar === 'novo' ? undefined : editar} aoFechar={(ok) => { setEditar(null); if (ok) void lista.recarregar() }} />}
    </div>
  )
}

function FormFuncionario({ funcionario, aoFechar }: { funcionario?: Funcionario; aoFechar: (ok?: boolean) => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const utilizadores = useDados(() => api.get<Opcao[]>('/api/funcionarios/utilizadores'), [])
  const veDados = tem('funcionarios.dados-pessoais')
  const [nome, setNome] = useState(funcionario?.nome ?? '')
  const [funcao, setFuncao] = useState<FuncaoFuncionario>(funcionario?.funcao ?? 'Garcom')
  const [telefone, setTelefone] = useState(funcionario?.telefone ?? '')
  const [email, setEmail] = useState(funcionario?.email ?? '')
  const [documento, setDocumento] = useState(funcionario?.documentoIdentificacao ?? '')
  const [admissao, setAdmissao] = useState(funcionario?.dataAdmissao ?? '')
  const [utilizadorId, setUtilizadorId] = useState<number | ''>(funcionario?.utilizadorId ?? '')
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const guardar = async () => {
    if (!nome.trim()) return setErro('Indique o nome.')
    setErro(null)
    setAGuardar(true)
    const corpo = {
      nome: nome.trim(), funcao, telefone: telefone.trim() || null, email: email.trim() || null,
      documentoIdentificacao: veDados ? documento.trim() || null : null, dataAdmissao: admissao || null, utilizadorId: utilizadorId === '' ? null : utilizadorId,
    }
    try {
      if (funcionario) await api.put(`/api/funcionarios/${funcionario.id}`, corpo)
      else await api.post('/api/funcionarios', corpo)
      avisos.sucesso(funcionario ? 'Funcionário actualizado.' : 'Funcionário registado.')
      aoFechar(true)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={funcionario ? `Editar ${funcionario.nome}` : 'Novo funcionário'} aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="grid gap-4 sm:grid-cols-2">
        {erro && <div className="sm:col-span-2"><Alerta tipo="erro">{erro}</Alerta></div>}
        <div className="sm:col-span-2"><label htmlFor="fnNome" className="rotulo">Nome</label><input id="fnNome" className="campo" maxLength={150} value={nome} onChange={(e) => setNome(e.target.value)} /></div>
        <div>
          <label htmlFor="fnFuncao" className="rotulo">Função</label>
          <select id="fnFuncao" className="campo" value={funcao} onChange={(e) => setFuncao(e.target.value as FuncaoFuncionario)}>
            {(Object.keys(NOME_FUNCAO) as FuncaoFuncionario[]).map((f) => <option key={f} value={f}>{NOME_FUNCAO[f]}</option>)}
          </select>
        </div>
        <div><label htmlFor="fnAdm" className="rotulo">Data de admissão</label><input id="fnAdm" type="date" className="campo" value={admissao} onChange={(e) => setAdmissao(e.target.value)} /></div>
        <div><label htmlFor="fnTel" className="rotulo">Telefone</label><input id="fnTel" type="tel" className="campo" maxLength={20} value={telefone} onChange={(e) => setTelefone(e.target.value)} /></div>
        <div><label htmlFor="fnMail" className="rotulo">E-mail</label><input id="fnMail" type="email" className="campo" maxLength={200} value={email} onChange={(e) => setEmail(e.target.value)} /></div>
        {veDados && (
          <div className="sm:col-span-2"><label htmlFor="fnDoc" className="rotulo">Documento de identificação (dado pessoal)</label><input id="fnDoc" className="campo" maxLength={50} value={documento} onChange={(e) => setDocumento(e.target.value)} /></div>
        )}
        <div className="sm:col-span-2">
          <label htmlFor="fnUser" className="rotulo">Utilizador do sistema (opcional)</label>
          <select id="fnUser" className="campo" value={utilizadorId} onChange={(e) => setUtilizadorId(e.target.value ? Number(e.target.value) : '')}>
            <option value="">Sem utilizador</option>
            {utilizadores.dados?.map((u) => <option key={u.id} value={u.id}>{u.nome}</option>)}
          </select>
        </div>
      </div>
    </Modal>
  )
}
