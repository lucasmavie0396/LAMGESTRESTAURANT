import { useState } from 'react'
import { ArrowDown, ArrowUp, CircleSlash, Pencil, Plus, Power, Search } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Confirmacao, Etiqueta, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Categoria, Pagina, Produto, TaxaImposto } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { P } from '../../utils/permissoes'
import { Categorias } from './Categorias'
import { FormularioProduto } from './FormularioProduto'

type Ordem = 'nome' | 'codigo' | 'preco' | 'categoria'
const TAMANHO = 25

export function PaginaProdutos() {
  const [separador, setSeparador] = useState<'produtos' | 'categorias'>('produtos')
  const { tem } = useSessao()

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <h1 className="text-2xl font-semibold">Produtos</h1>
      <div className="flex gap-1 border-b border-slate-200" role="tablist">
        {(['produtos', 'categorias'] as const).map((s) => (
          <button
            key={s}
            type="button"
            role="tab"
            aria-selected={separador === s}
            onClick={() => setSeparador(s)}
            className={`min-h-11 border-b-2 px-4 text-sm font-medium ${
              separador === s ? 'border-primaria-600 text-primaria-700' : 'border-transparent text-slate-600 hover:text-slate-900'
            }`}
          >
            {s === 'produtos' ? 'Produtos' : 'Categorias'}
          </button>
        ))}
      </div>
      {separador === 'produtos' ? <ListaProdutos /> : <Categorias podeGerir={tem(P.produtosGerir)} />}
    </div>
  )
}

function ListaProdutos() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const podeGerir = tem(P.produtosGerir)

  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const [categoriaId, setCategoriaId] = useState('')
  const [estado, setEstado] = useState<'' | 'activos' | 'inactivos' | 'esgotados'>('activos')
  const [ordem, setOrdem] = useState<{ campo: Ordem; desc: boolean }>({ campo: 'nome', desc: false })
  const [pagina, setPagina] = useState(1)
  const [formulario, setFormulario] = useState<Produto | 'novo' | null>(null)
  const [desactivar, setDesactivar] = useState<Produto | null>(null)
  const [aProcessar, setAProcessar] = useState(false)

  const filtrosEstado = {
    '': {},
    activos: { activo: true },
    inactivos: { activo: false },
    esgotados: { activo: true, disponivel: false },
  }[estado]

  const { dados, erro, aCarregar, recarregar } = useDados(
    () =>
      api.get<Pagina<Produto>>(
        '/api/produtos' +
          query({ pesquisa: pesquisaAtrasada, categoriaId, ...filtrosEstado, ordenar: ordem.campo, descendente: ordem.desc, pagina, tamanho: TAMANHO }),
      ),
    [pesquisaAtrasada, categoriaId, estado, ordem, pagina],
  )
  const { dados: categorias } = useDados(() => api.get<Categoria[]>('/api/categorias'), [])
  const { dados: taxas } = useDados(() => api.get<TaxaImposto[]>('/api/catalogo/taxas'), [])

  const mudarFiltro = <T,>(setter: (v: T) => void) => (v: T) => {
    setter(v)
    setPagina(1)
  }

  const alternarDisponivel = async (p: Produto) => {
    try {
      await api.post(`/api/produtos/${p.id}/disponibilidade`, { disponivel: !p.disponivel })
      avisos.sucesso(p.disponivel ? `${p.nome} marcado como esgotado.` : `${p.nome} disponível.`)
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  const alterarActivo = async (p: Produto, activo: boolean) => {
    setAProcessar(true)
    try {
      await api.post(`/api/produtos/${p.id}/activo`, { activo, rowVersion: p.rowVersion })
      avisos.sucesso(activo ? 'Produto activado.' : 'Produto desactivado.')
      setDesactivar(null)
      await recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAProcessar(false)
    }
  }

  const Cabecalho = ({ campo, rotulo, classe = '' }: { campo: Ordem; rotulo: string; classe?: string }) => (
    <th scope="col" className={`px-4 py-3 ${classe}`}>
      <button
        type="button"
        className="inline-flex items-center gap-1 font-semibold"
        onClick={() => mudarFiltro(setOrdem)({ campo, desc: ordem.campo === campo ? !ordem.desc : false })}
      >
        {rotulo}
        {ordem.campo === campo && (ordem.desc ? <ArrowDown className="h-4 w-4" /> : <ArrowUp className="h-4 w-4" />)}
      </button>
    </th>
  )

  return (
    <>
      <div className="cartao">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-200 p-4">
          <div className="relative min-w-60 flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
            <input
              type="search"
              className="campo pl-9"
              placeholder="Pesquisar por nome, código ou código de barras…"
              value={pesquisa}
              onChange={(e) => mudarFiltro(setPesquisa)(e.target.value)}
              aria-label="Pesquisar produtos"
            />
          </div>
          <select className="campo w-52" value={categoriaId} onChange={(e) => mudarFiltro(setCategoriaId)(e.target.value)} aria-label="Filtrar por categoria">
            <option value="">Todas as categorias</option>
            {categorias?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.categoriaPaiNome ? `${c.categoriaPaiNome} › ${c.nome}` : c.nome}
              </option>
            ))}
          </select>
          <select className="campo w-40" value={estado} onChange={(e) => mudarFiltro(setEstado)(e.target.value as typeof estado)} aria-label="Filtrar por estado">
            <option value="activos">Activos</option>
            <option value="esgotados">Esgotados</option>
            <option value="inactivos">Inactivos</option>
            <option value="">Todos</option>
          </select>
          {podeGerir && (
            <button type="button" className="btn-primario" onClick={() => setFormulario('novo')} disabled={!podeGravar || !categorias?.length}>
              <Plus className="h-4 w-4" aria-hidden /> Novo produto
            </button>
          )}
        </div>

        {podeGerir && categorias && categorias.length === 0 && (
          <div className="p-4">
            <Alerta tipo="info">Crie primeiro uma categoria (separador «Categorias») para poder registar produtos.</Alerta>
          </div>
        )}
        {erro && (
          <div className="p-4">
            <Alerta tipo="erro">{erro}</Alerta>
          </div>
        )}

        {aCarregar && !dados ? (
          <Carregando />
        ) : dados && dados.itens.length === 0 ? (
          <Vazio texto={pesquisaAtrasada || categoriaId ? 'Nenhum produto corresponde aos filtros.' : 'Ainda não há produtos registados.'} />
        ) : (
          dados && (
            <div className={`overflow-x-auto ${aCarregar ? 'opacity-60' : ''}`}>
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-slate-600">
                  <tr>
                    <Cabecalho campo="codigo" rotulo="Código" />
                    <Cabecalho campo="nome" rotulo="Nome" />
                    <Cabecalho campo="categoria" rotulo="Categoria" />
                    <Cabecalho campo="preco" rotulo="Preço" classe="text-right" />
                    <th scope="col" className="px-4 py-3 font-semibold">Imposto</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Estado</th>
                    {podeGerir && <th scope="col" className="px-4 py-3 text-right font-semibold">Acções</th>}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {dados.itens.map((p) => (
                    <tr key={p.id} className="hover:bg-slate-50">
                      <td className="px-4 py-2 font-mono text-xs text-slate-600">{p.codigo}</td>
                      <td className="px-4 py-2">
                        <p className="font-medium">{p.nome}</p>
                        <p className="text-xs text-slate-500">
                          {p.tipo}
                          {!p.enviaCozinha && ' · não vai à cozinha'}
                          {p.codigoBarras && ` · ${p.codigoBarras}`}
                        </p>
                      </td>
                      <td className="px-4 py-2 text-slate-600">{p.categoriaNome}</td>
                      <td className="px-4 py-2 text-right font-medium tabular-nums">{formatarMoeda(p.precoVenda)}</td>
                      <td className="px-4 py-2 text-slate-600">{p.taxaImpostoNome ?? '—'}</td>
                      <td className="px-4 py-2">
                        <div className="flex flex-wrap gap-1">
                          {!p.activo ? <Etiqueta cor="cinza">Inactivo</Etiqueta> : p.disponivel ? <Etiqueta cor="verde">Disponível</Etiqueta> : <Etiqueta cor="amarelo">Esgotado</Etiqueta>}
                        </div>
                      </td>
                      {podeGerir && (
                        <td className="px-4 py-2">
                          <div className="flex justify-end">
                            <button type="button" className="btn-icone" title="Editar" aria-label={`Editar ${p.nome}`} disabled={!podeGravar} onClick={() => setFormulario(p)}>
                              <Pencil className="h-4 w-4" />
                            </button>
                            {p.activo && (
                              <button type="button" className="btn-icone" title={p.disponivel ? 'Marcar como esgotado' : 'Marcar como disponível'}
                                aria-label={p.disponivel ? `Marcar ${p.nome} como esgotado` : `Marcar ${p.nome} como disponível`}
                                disabled={!podeGravar} onClick={() => void alternarDisponivel(p)}>
                                <CircleSlash className={`h-4 w-4 ${p.disponivel ? '' : 'text-amber-600'}`} />
                              </button>
                            )}
                            <button type="button" className="btn-icone" title={p.activo ? 'Desactivar' : 'Activar'} aria-label={`${p.activo ? 'Desactivar' : 'Activar'} ${p.nome}`}
                              disabled={!podeGravar || aProcessar} onClick={() => (p.activo ? setDesactivar(p) : void alterarActivo(p, true))}>
                              <Power className={`h-4 w-4 ${p.activo ? 'text-red-600' : 'text-green-700'}`} />
                            </button>
                          </div>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}
        {dados && dados.total > 0 && <Paginacao pagina={pagina} tamanho={TAMANHO} total={dados.total} aoMudar={setPagina} />}
      </div>

      {formulario && categorias && (
        <FormularioProduto
          produto={formulario === 'novo' ? undefined : formulario}
          categorias={categorias}
          taxas={taxas ?? []}
          podeAlterarPreco={tem(P.produtosAlterarPreco)}
          podeLancarStock={tem('stock.ajustar')}
          aoFechar={() => setFormulario(null)}
          aoGuardar={async () => {
            setFormulario(null)
            await recarregar()
          }}
        />
      )}

      {desactivar && (
        <Confirmacao
          titulo="Desactivar produto"
          mensagem={
            <>
              <strong>{desactivar.nome}</strong> deixa de aparecer no POS. O histórico de vendas mantém-se. Pode reactivá-lo mais tarde.
            </>
          }
          textoConfirmar="Desactivar"
          perigo
          aProcessar={aProcessar}
          aoCancelar={() => setDesactivar(null)}
          aoConfirmar={() => void alterarActivo(desactivar, false)}
        />
      )}
    </>
  )
}
