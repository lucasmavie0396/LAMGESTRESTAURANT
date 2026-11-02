import { useCallback, useEffect, useMemo, useState } from 'react'
import { Loader2, Pencil, Plus, ShieldCheck, Trash2 } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { Alerta, Carregando, Confirmacao, Etiqueta, Modal, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import type { PerfilResumo, PermissaoInfo } from '../../types/api'
import { NOMES_MODULOS } from '../../utils/permissoes'

const ADMINISTRADOR = 'Administrador'

export function PaginaPerfis() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [perfis, setPerfis] = useState<PerfilResumo[] | null>(null)
  const [catalogo, setCatalogo] = useState<PermissaoInfo[]>([])
  const [erro, setErro] = useState<string | null>(null)
  const [editar, setEditar] = useState<PerfilResumo | 'novo' | null>(null)
  const [eliminar, setEliminar] = useState<PerfilResumo | null>(null)
  const [aEliminar, setAEliminar] = useState(false)

  const carregar = useCallback(async () => {
    try {
      const [p, c] = await Promise.all([api.get<PerfilResumo[]>('/api/perfis'), api.get<PermissaoInfo[]>('/api/perfis/permissoes')])
      setPerfis(p)
      setCatalogo(c)
      setErro(null)
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível carregar os perfis.')
    }
  }, [])

  useEffect(() => {
    void carregar()
  }, [carregar])

  const confirmarEliminar = async () => {
    if (!eliminar) return
    setAEliminar(true)
    try {
      await api.delete(`/api/perfis/${eliminar.id}`)
      avisos.sucesso('Perfil eliminado.')
      setEliminar(null)
      await carregar()
    } catch (e) {
      avisos.erro(e instanceof ErroApi ? e.message : 'Não foi possível eliminar o perfil.')
    } finally {
      setAEliminar(false)
    }
  }

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Perfis e permissões</h1>
        <button type="button" className="btn-primario" onClick={() => setEditar('novo')} disabled={!podeGravar || catalogo.length === 0}>
          <Plus className="h-4 w-4" aria-hidden /> Novo perfil
        </button>
      </div>

      {erro && <Alerta tipo="erro">{erro}</Alerta>}
      {!perfis && !erro && <Carregando />}
      {perfis && perfis.length === 0 && <Vazio texto="Não existem perfis." />}

      {perfis && perfis.length > 0 && (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {perfis.map((p) => (
            <div key={p.id} className="cartao flex flex-col p-5">
              <div className="flex items-start gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primaria-100 text-primaria-700">
                  <ShieldCheck className="h-5 w-5" aria-hidden />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="font-semibold">{p.nome}</h2>
                    {p.sistema && <Etiqueta cor="cinza">Sistema</Etiqueta>}
                  </div>
                  {p.descricao && <p className="text-sm text-slate-600">{p.descricao}</p>}
                </div>
              </div>
              <p className="mt-3 text-sm text-slate-600">
                {p.permissoes.length} permissões · {p.numeroUtilizadores} {p.numeroUtilizadores === 1 ? 'utilizador' : 'utilizadores'}
              </p>
              <div className="mt-4 flex gap-2">
                <button type="button" className="btn-secundario flex-1" onClick={() => setEditar(p)} disabled={!podeGravar}>
                  <Pencil className="h-4 w-4" aria-hidden /> {p.nome === ADMINISTRADOR ? 'Ver' : 'Editar'}
                </button>
                {!p.sistema && (
                  <button type="button" className="btn-icone" title="Eliminar perfil" aria-label={`Eliminar perfil ${p.nome}`}
                    disabled={!podeGravar || p.numeroUtilizadores > 0} onClick={() => setEliminar(p)}>
                    <Trash2 className="h-4 w-4 text-red-600" />
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {editar && (
        <EditorPerfil
          perfil={editar === 'novo' ? undefined : editar}
          catalogo={catalogo}
          aoFechar={() => setEditar(null)}
          aoGuardar={async () => {
            setEditar(null)
            await carregar()
          }}
        />
      )}

      {eliminar && (
        <Confirmacao
          titulo="Eliminar perfil"
          mensagem={<>Eliminar o perfil <strong>{eliminar.nome}</strong>? Esta acção não pode ser desfeita.</>}
          textoConfirmar="Eliminar"
          perigo
          aProcessar={aEliminar}
          aoCancelar={() => setEliminar(null)}
          aoConfirmar={() => void confirmarEliminar()}
        />
      )}
    </div>
  )
}

function EditorPerfil({
  perfil,
  catalogo,
  aoFechar,
  aoGuardar,
}: {
  perfil?: PerfilResumo
  catalogo: PermissaoInfo[]
  aoFechar: () => void
  aoGuardar: () => void
}) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const eAdministrador = perfil?.nome === ADMINISTRADOR
  const [nome, setNome] = useState(perfil?.nome ?? '')
  const [descricao, setDescricao] = useState(perfil?.descricao ?? '')
  const [seleccao, setSeleccao] = useState<Set<string>>(() => new Set(perfil?.permissoes ?? []))
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)

  const modulos = useMemo(() => {
    const m = new Map<string, PermissaoInfo[]>()
    for (const p of catalogo) m.set(p.modulo, [...(m.get(p.modulo) ?? []), p])
    return [...m.entries()]
  }, [catalogo])

  const alternar = (codigos: string[], marcar: boolean) =>
    setSeleccao((s) => {
      const n = new Set(s)
      for (const c of codigos) {
        if (marcar) n.add(c)
        else n.delete(c)
      }
      return n
    })

  const guardar = async () => {
    if (nome.trim().length < 2) {
      setErro('O nome do perfil deve ter pelo menos 2 caracteres.')
      return
    }
    setErro(null)
    setAGuardar(true)
    const corpo = { nome: nome.trim(), descricao: descricao.trim() || null, permissoes: [...seleccao] }
    try {
      if (perfil) await api.put(`/api/perfis/${perfil.id}`, corpo)
      else await api.post('/api/perfis', corpo)
      avisos.sucesso(perfil ? 'Perfil actualizado. As sessões abertas reflectem a alteração em até 1 minuto.' : 'Perfil criado.')
      aoGuardar()
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível guardar o perfil.')
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal
      titulo={perfil ? `Perfil: ${perfil.nome}` : 'Novo perfil'}
      aoFechar={aoFechar}
      largura="max-w-3xl"
      rodape={
        <>
          <button type="button" className="btn-secundario" onClick={aoFechar}>{eAdministrador ? 'Fechar' : 'Cancelar'}</button>
          {!eAdministrador && (
            <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
              {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
              Guardar
            </button>
          )}
        </>
      }
    >
      <div className="space-y-4">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        {eAdministrador && <Alerta tipo="info">O perfil Administrador tem sempre todas as permissões e não pode ser alterado.</Alerta>}
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <label htmlFor="nomePerfil" className="rotulo">Nome</label>
            <input id="nomePerfil" className="campo" value={nome} onChange={(e) => setNome(e.target.value)} disabled={perfil?.sistema} maxLength={50} />
            {perfil?.sistema && <p className="mt-1 text-xs text-slate-500">Perfis iniciais do sistema não podem ser renomeados.</p>}
          </div>
          <div>
            <label htmlFor="descricaoPerfil" className="rotulo">Descrição</label>
            <input id="descricaoPerfil" className="campo" value={descricao} onChange={(e) => setDescricao(e.target.value)} disabled={eAdministrador} maxLength={500} />
          </div>
        </div>

        <div className="space-y-3">
          {modulos.map(([modulo, permissoes]) => {
            const codigos = permissoes.map((p) => p.codigo)
            const todas = codigos.every((c) => seleccao.has(c))
            return (
              <fieldset key={modulo} className="rounded-lg border border-slate-200 p-3">
                <legend className="px-1">
                  <label className="flex items-center gap-2 text-sm font-semibold">
                    <input type="checkbox" className="h-4 w-4" checked={todas} disabled={eAdministrador}
                      onChange={(e) => alternar(codigos, e.target.checked)} />
                    {NOMES_MODULOS[modulo] ?? modulo}
                  </label>
                </legend>
                <div className="grid gap-1 sm:grid-cols-2">
                  {permissoes.map((p) => (
                    <label key={p.codigo} className="flex min-h-9 items-center gap-2 text-sm">
                      <input type="checkbox" className="h-4 w-4" checked={seleccao.has(p.codigo)} disabled={eAdministrador}
                        onChange={(e) => alternar([p.codigo], e.target.checked)} />
                      {p.descricao}
                    </label>
                  ))}
                </div>
              </fieldset>
            )
          })}
        </div>
      </div>
    </Modal>
  )
}
