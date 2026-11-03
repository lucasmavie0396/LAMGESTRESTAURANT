import { useCallback, useEffect, useState } from 'react'
import { ArrowDown, ArrowUp, Grid3x3, KeyRound, Lock, Pencil, Plus, Power, Search, Unlock } from 'lucide-react'
import { api, ErroApi, query } from '../../api/cliente'
import { Alerta, Carregando, Confirmacao, Etiqueta, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import type { Pagina, PerfilResumo, UtilizadorResumo } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'
import { CodigoTactilModal, FormularioUtilizador, ReporPalavraPasseModal } from './FormularioUtilizador'

type Ordem = 'nomeCompleto' | 'nomeUtilizador' | 'ultimoLogin'
const TAMANHO = 25

type Dialogo =
  | { tipo: 'novo' }
  | { tipo: 'editar'; utilizador: UtilizadorResumo }
  | { tipo: 'repor'; utilizador: UtilizadorResumo }
  | { tipo: 'estado'; utilizador: UtilizadorResumo }
  | { tipo: 'pin'; utilizador: UtilizadorResumo }
  | null

export function PaginaUtilizadores() {
  const { utilizador: eu } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()

  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const [filtroActivo, setFiltroActivo] = useState<'' | 'true' | 'false'>('')
  const [ordem, setOrdem] = useState<{ campo: Ordem; desc: boolean }>({ campo: 'nomeCompleto', desc: false })
  const [pagina, setPagina] = useState(1)

  const [dados, setDados] = useState<Pagina<UtilizadorResumo> | null>(null)
  const [perfis, setPerfis] = useState<PerfilResumo[]>([])
  const [erro, setErro] = useState<string | null>(null)
  const [aCarregar, setACarregar] = useState(true)
  const [dialogo, setDialogo] = useState<Dialogo>(null)
  const [aProcessar, setAProcessar] = useState(false)

  const carregar = useCallback(async () => {
    setACarregar(true)
    try {
      const resultado = await api.get<Pagina<UtilizadorResumo>>(
        '/api/utilizadores' +
          query({ pesquisa: pesquisaAtrasada, activo: filtroActivo, ordenar: ordem.campo, descendente: ordem.desc, pagina, tamanho: TAMANHO }),
      )
      setDados(resultado)
      setErro(null)
    } catch (e) {
      setErro(e instanceof ErroApi ? e.message : 'Não foi possível carregar os utilizadores.')
    } finally {
      setACarregar(false)
    }
  }, [pesquisaAtrasada, filtroActivo, ordem, pagina])

  useEffect(() => {
    void carregar()
  }, [carregar])

  useEffect(() => {
    api.get<PerfilResumo[]>('/api/perfis').then(setPerfis).catch(() => setPerfis([]))
  }, [])

  // Ao mudar filtros volta-se à primeira página.
  useEffect(() => setPagina(1), [pesquisaAtrasada, filtroActivo, ordem])

  const ordenarPor = (campo: Ordem) =>
    setOrdem((o) => ({ campo, desc: o.campo === campo ? !o.desc : false }))

  const executar = async (accao: () => Promise<unknown>, sucesso: string) => {
    setAProcessar(true)
    try {
      await accao()
      avisos.sucesso(sucesso)
      setDialogo(null)
      await carregar()
    } catch (e) {
      avisos.erro(e instanceof ErroApi ? e.message : 'A operação falhou.')
    } finally {
      setAProcessar(false)
    }
  }

  const Cabecalho = ({ campo, rotulo }: { campo: Ordem; rotulo: string }) => (
    <th scope="col" className="px-4 py-3">
      <button type="button" className="inline-flex items-center gap-1 font-semibold" onClick={() => ordenarPor(campo)}>
        {rotulo}
        {ordem.campo === campo && (ordem.desc ? <ArrowDown className="h-4 w-4" /> : <ArrowUp className="h-4 w-4" />)}
      </button>
    </th>
  )

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Utilizadores</h1>
        <button type="button" className="btn-primario" onClick={() => setDialogo({ tipo: 'novo' })} disabled={!podeGravar}>
          <Plus className="h-4 w-4" aria-hidden /> Novo utilizador
        </button>
      </div>

      <div className="cartao">
        <div className="flex flex-wrap gap-3 border-b border-slate-200 p-4">
          <div className="relative min-w-60 flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
            <input
              type="search"
              className="campo pl-9"
              placeholder="Pesquisar por nome ou utilizador…"
              value={pesquisa}
              onChange={(e) => setPesquisa(e.target.value)}
              aria-label="Pesquisar utilizadores"
            />
          </div>
          <select className="campo w-44" value={filtroActivo} onChange={(e) => setFiltroActivo(e.target.value as typeof filtroActivo)} aria-label="Filtrar por estado">
            <option value="">Todos</option>
            <option value="true">Activos</option>
            <option value="false">Inactivos</option>
          </select>
        </div>

        {erro && (
          <div className="p-4">
            <Alerta tipo="erro">{erro}</Alerta>
          </div>
        )}

        {aCarregar && !dados ? (
          <Carregando />
        ) : dados && dados.itens.length === 0 ? (
          <Vazio texto={pesquisaAtrasada || filtroActivo ? 'Nenhum utilizador corresponde à pesquisa.' : 'Ainda não existem utilizadores.'} />
        ) : (
          dados && (
            <div className={`overflow-x-auto ${aCarregar ? 'opacity-60' : ''}`}>
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-slate-600">
                  <tr>
                    <Cabecalho campo="nomeCompleto" rotulo="Nome" />
                    <Cabecalho campo="nomeUtilizador" rotulo="Utilizador" />
                    <th scope="col" className="px-4 py-3 font-semibold">Perfis</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Estado</th>
                    <Cabecalho campo="ultimoLogin" rotulo="Último acesso" />
                    <th scope="col" className="px-4 py-3 text-right font-semibold">Acções</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {dados.itens.map((u) => (
                    <tr key={u.id} className="hover:bg-slate-50">
                      <td className="px-4 py-2 font-medium">{u.nomeCompleto}</td>
                      <td className="px-4 py-2 text-slate-600">{u.nomeUtilizador}</td>
                      <td className="px-4 py-2">
                        <div className="flex flex-wrap gap-1">
                          {u.perfis.map((p) => (
                            <Etiqueta key={p} cor="azul">{p}</Etiqueta>
                          ))}
                        </div>
                      </td>
                      <td className="px-4 py-2">
                        <div className="flex flex-wrap gap-1">
                          {u.activo ? <Etiqueta cor="verde">Activo</Etiqueta> : <Etiqueta cor="cinza">Inactivo</Etiqueta>}
                          {u.bloqueadoAteUtc && <Etiqueta cor="vermelho">Bloqueado</Etiqueta>}
                          {u.deveAlterarPalavraPasse && <Etiqueta cor="amarelo">Palavra-passe temporária</Etiqueta>}
                          {u.ecraTactil ? <Etiqueta cor="azul">Ecrã táctil</Etiqueta> : u.temPin && <Etiqueta cor="cinza">Com código</Etiqueta>}
                        </div>
                      </td>
                      <td className="px-4 py-2 text-slate-600">{u.ultimoLoginUtc ? formatarDataHora(u.ultimoLoginUtc) : '—'}</td>
                      <td className="px-4 py-2">
                        <div className="flex justify-end">
                          <button type="button" className="btn-icone" title="Editar" aria-label={`Editar ${u.nomeCompleto}`} disabled={!podeGravar}
                            onClick={() => setDialogo({ tipo: 'editar', utilizador: u })}>
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button type="button" className="btn-icone" title="Repor palavra-passe" aria-label={`Repor palavra-passe de ${u.nomeCompleto}`} disabled={!podeGravar}
                            onClick={() => setDialogo({ tipo: 'repor', utilizador: u })}>
                            <KeyRound className="h-4 w-4" />
                          </button>
                          <button type="button" className="btn-icone" title="Código do ecrã táctil" aria-label={`Código do ecrã táctil de ${u.nomeCompleto}`} disabled={!podeGravar}
                            onClick={() => setDialogo({ tipo: 'pin', utilizador: u })}>
                            <Grid3x3 className="h-4 w-4" />
                          </button>
                          {u.bloqueadoAteUtc && (
                            <button type="button" className="btn-icone" title="Desbloquear" aria-label={`Desbloquear ${u.nomeCompleto}`} disabled={!podeGravar || aProcessar}
                              onClick={() => void executar(() => api.post(`/api/utilizadores/${u.id}/desbloquear`), 'Conta desbloqueada.')}>
                              <Unlock className="h-4 w-4" />
                            </button>
                          )}
                          <button type="button" className="btn-icone" title={u.activo ? 'Desactivar' : 'Activar'}
                            aria-label={`${u.activo ? 'Desactivar' : 'Activar'} ${u.nomeCompleto}`}
                            disabled={!podeGravar || u.id === eu?.id}
                            onClick={() => setDialogo({ tipo: 'estado', utilizador: u })}>
                            {u.activo ? <Power className="h-4 w-4 text-red-600" /> : <Lock className="h-4 w-4 text-green-700" />}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}
        {dados && dados.total > 0 && <Paginacao pagina={pagina} tamanho={TAMANHO} total={dados.total} aoMudar={setPagina} />}
      </div>

      {(dialogo?.tipo === 'novo' || dialogo?.tipo === 'editar') && (
        <FormularioUtilizador
          utilizador={dialogo.tipo === 'editar' ? dialogo.utilizador : undefined}
          perfis={perfis}
          aoFechar={() => setDialogo(null)}
          aoGuardar={async () => {
            setDialogo(null)
            await carregar()
          }}
        />
      )}

      {dialogo?.tipo === 'repor' && (
        <ReporPalavraPasseModal
          utilizador={dialogo.utilizador}
          aoFechar={() => setDialogo(null)}
          aoGuardar={async () => {
            setDialogo(null)
            await carregar()
          }}
        />
      )}

      {dialogo?.tipo === 'pin' && (
        <CodigoTactilModal
          utilizador={dialogo.utilizador}
          aoFechar={() => setDialogo(null)}
          aoGuardar={async () => {
            setDialogo(null)
            await carregar()
          }}
        />
      )}

      {dialogo?.tipo === 'estado' && (
        <Confirmacao
          titulo={dialogo.utilizador.activo ? 'Desactivar utilizador' : 'Activar utilizador'}
          mensagem={
            dialogo.utilizador.activo ? (
              <>
                <strong>{dialogo.utilizador.nomeCompleto}</strong> deixa de poder iniciar sessão e as sessões abertas terminam. O histórico
                do utilizador mantém-se.
              </>
            ) : (
              <>
                <strong>{dialogo.utilizador.nomeCompleto}</strong> volta a poder iniciar sessão.
              </>
            )
          }
          textoConfirmar={dialogo.utilizador.activo ? 'Desactivar' : 'Activar'}
          perigo={dialogo.utilizador.activo}
          aProcessar={aProcessar}
          aoCancelar={() => setDialogo(null)}
          aoConfirmar={() => {
            const u = dialogo.utilizador
            void executar(
              () => api.post(`/api/utilizadores/${u.id}/${u.activo ? 'desactivar' : 'activar'}`),
              u.activo ? 'Utilizador desactivado.' : 'Utilizador activado.',
            )
          }}
        />
      )}
    </div>
  )
}
