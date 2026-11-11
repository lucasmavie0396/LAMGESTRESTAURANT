import { useState } from 'react'
import { Link } from 'react-router-dom'
import { FileClock, Loader2, Pencil, Plus, Power, ReceiptText, Search, Star } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta, Modal, Paginacao, Vazio } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useSessao } from '../../contexts/SessaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Cliente, ContaCorrente, MovimentoPontos, Pagina, PedidoCliente, TipoCliente } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'

export function PaginaClientes() {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [pesquisa, setPesquisa] = useState('')
  const pesquisaAtrasada = useAtraso(pesquisa)
  const [inactivos, setInactivos] = useState(false)
  const [pagina, setPagina] = useState(1)
  const [editar, setEditar] = useState<Cliente | 'novo' | null>(null)
  const [detalhe, setDetalhe] = useState<Cliente | null>(null)
  const lista = useDados(() => api.get<Pagina<Cliente>>('/api/clientes' + query({ pesquisa: pesquisaAtrasada, activo: !inactivos, pagina, tamanho: 25 })),
    [pesquisaAtrasada, inactivos, pagina])

  const alternar = async (c: Cliente) => {
    try {
      await api.post(`/api/clientes/${c.id}/activo`, { activo: !c.activo })
      avisos.sucesso(c.activo ? 'Cliente desactivado.' : 'Cliente activado.')
      await lista.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }

  return (
    <div className="mx-auto max-w-7xl space-y-4">
      <h1 className="text-2xl font-semibold">Clientes</h1>
      <div className="cartao">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-200 p-4">
          <div className="relative min-w-60 flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
            <input type="search" className="campo pl-9" placeholder="Nome, telefone, NUIT ou cartão…" value={pesquisa}
              onChange={(e) => { setPesquisa(e.target.value); setPagina(1) }} aria-label="Pesquisar clientes" />
          </div>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4" checked={inactivos} onChange={(e) => { setInactivos(e.target.checked); setPagina(1) }} /> Mostrar inactivos</label>
          {tem('clientes.gerir') && (
            <button type="button" className="btn-primario" onClick={() => setEditar('novo')} disabled={!podeGravar}><Plus className="h-4 w-4" aria-hidden /> Novo cliente</button>
          )}
        </div>
        {lista.erro && <div className="p-4"><Alerta tipo="erro">{lista.erro}</Alerta></div>}
        {!lista.dados && !lista.erro && <Carregando />}
        {lista.dados?.itens.length === 0 && <Vazio texto="Sem clientes." />}
        {lista.dados && lista.dados.itens.length > 0 && (
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-600">
              <tr>
                <th scope="col" className="px-4 py-3">Cliente</th>
                <th scope="col" className="px-4 py-3">Contactos</th>
                <th scope="col" className="px-4 py-3 text-right">Pedidos pagos</th>
                <th scope="col" className="px-4 py-3 text-right">Pontos</th>
                <th scope="col" className="px-4 py-3 text-right">Acções</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {lista.dados.itens.map((c) => (
                <tr key={c.id} className="cursor-pointer hover:bg-slate-50" onClick={() => setDetalhe(c)}>
                  <td className="px-4 py-2">
                    <p className="font-medium">{c.nome}</p>
                    <p className="text-xs text-slate-500">{c.tipo === 'Empresa' ? 'Empresa' : 'Particular'}{c.nuit && ` · NUIT ${c.nuit}`}{c.cartaoFidelidade && ` · cartão ${c.cartaoFidelidade}`}</p>
                  </td>
                  <td className="px-4 py-2 text-slate-600">{[c.telefone, c.email].filter(Boolean).join(' · ') || '—'}</td>
                  <td className="px-4 py-2 text-right">{c.numeroPedidos} · {formatarMoeda(c.totalGasto)}</td>
                  <td className="px-4 py-2 text-right">{c.saldoPontos}</td>
                  <td className="px-4 py-2" onClick={(e) => e.stopPropagation()}>
                    {tem('clientes.gerir') && (
                      <div className="flex justify-end">
                        <button type="button" className="btn-icone" aria-label={`Editar ${c.nome}`} disabled={!podeGravar} onClick={() => setEditar(c)}><Pencil className="h-4 w-4" /></button>
                        <button type="button" className="btn-icone" aria-label={`${c.activo ? 'Desactivar' : 'Activar'} ${c.nome}`} disabled={!podeGravar} onClick={() => void alternar(c)}>
                          <Power className={`h-4 w-4 ${c.activo ? 'text-red-600' : 'text-green-700'}`} />
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {lista.dados && lista.dados.total > 0 && <Paginacao pagina={pagina} tamanho={25} total={lista.dados.total} aoMudar={setPagina} />}
      </div>
      {editar && <FormCliente cliente={editar === 'novo' ? undefined : editar} aoFechar={(c) => { setEditar(null); if (c) void lista.recarregar() }} />}
      {detalhe && <DetalheCliente cliente={detalhe} aoFechar={() => { setDetalhe(null); void lista.recarregar() }} />}
    </div>
  )
}

/** Formulário de cliente; também usado no POS para criar o cliente ao associá-lo ao pedido. */
export function FormCliente({ cliente, nomeInicial, aoFechar }: { cliente?: Cliente; nomeInicial?: string; aoFechar: (guardado?: Cliente) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [tipo, setTipo] = useState<TipoCliente>(cliente?.tipo ?? 'Particular')
  const [d, setD] = useState({
    nome: cliente?.nome ?? nomeInicial ?? '', telefone: cliente?.telefone ?? '', email: cliente?.email ?? '', endereco: cliente?.endereco ?? '',
    nuit: cliente?.nuit ?? '', cartaoFidelidade: cliente?.cartaoFidelidade ?? '', observacoes: cliente?.observacoes ?? '',
  })
  const [erro, setErro] = useState<string | null>(null)
  const [aGuardar, setAGuardar] = useState(false)
  const campo = (k: keyof typeof d) => ({ value: d[k], onChange: (e: { target: { value: string } }) => setD((x) => ({ ...x, [k]: e.target.value })) })

  const guardar = async () => {
    if (!d.nome.trim()) return setErro('Indique o nome.')
    if (d.nuit && !/^\d{9}$/.test(d.nuit.trim())) return setErro('O NUIT tem 9 algarismos.')
    setErro(null)
    setAGuardar(true)
    const corpo = { tipo, ...Object.fromEntries(Object.entries(d).map(([k, v]) => [k, v.trim() || null])), rowVersion: cliente?.rowVersion ?? null }
    try {
      const guardado = cliente ? await api.put<Cliente>(`/api/clientes/${cliente.id}`, corpo) : await api.post<Cliente>('/api/clientes', corpo)
      avisos.sucesso(cliente ? 'Cliente actualizado.' : 'Cliente criado.')
      aoFechar(guardado)
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={cliente ? `Editar ${cliente.nome}` : 'Novo cliente'} aoFechar={() => aoFechar()}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="grid gap-4 sm:grid-cols-2">
        {erro && <div className="sm:col-span-2"><Alerta tipo="erro">{erro}</Alerta></div>}
        <div className="flex gap-2 sm:col-span-2">
          {(['Particular', 'Empresa'] as const).map((t) => (
            <button key={t} type="button" className={`${tipo === t ? 'btn-primario' : 'btn-secundario'} flex-1`} onClick={() => setTipo(t)}>{t}</button>
          ))}
        </div>
        <div className="sm:col-span-2"><label htmlFor="cNome" className="rotulo">Nome</label><input id="cNome" className="campo" maxLength={150} {...campo('nome')} /></div>
        <div><label htmlFor="cTel" className="rotulo">Telefone</label><input id="cTel" type="tel" className="campo" maxLength={20} placeholder="84 000 0000" {...campo('telefone')} /></div>
        <div><label htmlFor="cMail" className="rotulo">E-mail</label><input id="cMail" type="email" className="campo" maxLength={200} {...campo('email')} /></div>
        <div><label htmlFor="cNuit" className="rotulo">NUIT</label><input id="cNuit" className="campo" inputMode="numeric" maxLength={9} {...campo('nuit')} /></div>
        <div><label htmlFor="cCartao" className="rotulo">Cartão de fidelidade</label><input id="cCartao" className="campo" maxLength={30} {...campo('cartaoFidelidade')} /></div>
        <div className="sm:col-span-2"><label htmlFor="cEnd" className="rotulo">Endereço</label><input id="cEnd" className="campo" maxLength={1000} {...campo('endereco')} /></div>
        <div className="sm:col-span-2"><label htmlFor="cObs" className="rotulo">Observações (ex.: alergias, preferências)</label><textarea id="cObs" rows={2} className="campo py-2" maxLength={1000} {...campo('observacoes')} /></div>
      </div>
    </Modal>
  )
}

function DetalheCliente({ cliente, aoFechar }: { cliente: Cliente; aoFechar: () => void }) {
  const { tem } = useSessao()
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const actual = useDados(() => api.get<Cliente>(`/api/clientes/${cliente.id}`), [cliente.id])
  const pedidos = useDados(() => api.get<PedidoCliente[]>(`/api/clientes/${cliente.id}/pedidos`), [cliente.id])
  const pontos = useDados(() => api.get<MovimentoPontos[]>(`/api/clientes/${cliente.id}/pontos`), [cliente.id])
  const [ajuste, setAjuste] = useState('')
  const [motivo, setMotivo] = useState('')
  const [aGravar, setAGravar] = useState(false)
  const c = actual.dados ?? cliente

  const ajustar = async () => {
    setAGravar(true)
    try {
      await api.post(`/api/clientes/${c.id}/pontos`, { pontos: Number(ajuste), motivo: motivo.trim(), rowVersion: c.rowVersion })
      avisos.sucesso('Pontos ajustados.')
      setAjuste('')
      setMotivo('')
      await Promise.all([actual.recarregar(), pontos.recarregar()])
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGravar(false)
    }
  }

  return (
    <Modal titulo={c.nome} aoFechar={aoFechar} largura="max-w-3xl" rodape={<button type="button" className="btn-primario" onClick={aoFechar}>Fechar</button>}>
      <div className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-3">
          <div className="rounded-lg bg-slate-50 p-3"><p className="text-xs text-slate-500">Pedidos pagos</p><p className="text-lg font-semibold">{c.numeroPedidos}</p></div>
          <div className="rounded-lg bg-slate-50 p-3"><p className="text-xs text-slate-500">Total gasto</p><p className="text-lg font-semibold">{formatarMoeda(c.totalGasto)}</p></div>
          <div className="rounded-lg bg-slate-50 p-3"><p className="flex items-center gap-1 text-xs text-slate-500"><Star className="h-3.5 w-3.5" aria-hidden /> Pontos</p><p className="text-lg font-semibold">{c.saldoPontos}</p></div>
        </div>
        {c.observacoes && <Alerta tipo="info">{c.observacoes}</Alerta>}
        <ContaCorrenteCliente clienteId={c.id} />
        <section>
          <h3 className="mb-2 font-semibold">Últimos pedidos</h3>
          {pedidos.dados?.length === 0 && <p className="text-sm text-slate-500">Sem pedidos.</p>}
          <ul className="max-h-48 divide-y divide-slate-100 overflow-auto text-sm">
            {pedidos.dados?.map((p) => (
              <li key={p.id} className="flex justify-between py-1.5">
                <span>{p.numero} · {formatarDataHora(p.criadoEmUtc)}</span>
                <span className="flex items-center gap-2 tabular-nums">{formatarMoeda(p.total)} <Etiqueta cor={p.estado === 'Pago' ? 'verde' : p.estado === 'Aberto' ? 'azul' : 'cinza'}>{p.estado}</Etiqueta></span>
              </li>
            ))}
          </ul>
        </section>
        <section>
          <h3 className="mb-2 font-semibold">Movimentos de pontos</h3>
          {pontos.dados?.length === 0 && <p className="text-sm text-slate-500">Sem movimentos. Os pontos são ganhos ao pagar pedidos com o cliente associado, se a fidelização estiver activa nas Configurações.</p>}
          <ul className="max-h-40 divide-y divide-slate-100 overflow-auto text-sm">
            {pontos.dados?.map((m) => (
              <li key={m.id} className="flex justify-between py-1.5">
                <span>{formatarDataHora(m.dataUtc)} · {m.tipo}{m.motivo && ` — ${m.motivo}`}</span>
                <span className={`font-medium tabular-nums ${m.pontos < 0 ? 'text-red-700' : 'text-green-700'}`}>{m.pontos > 0 ? '+' : ''}{m.pontos}</span>
              </li>
            ))}
          </ul>
          {tem('clientes.gerir') && (
            <div className="mt-3 flex flex-wrap items-end gap-2">
              <div className="w-28"><label htmlFor="ajPontos" className="rotulo">Ajuste</label><input id="ajPontos" type="number" step="1" className="campo" value={ajuste} onChange={(e) => setAjuste(e.target.value)} placeholder="+10 / −5" /></div>
              <div className="min-w-48 flex-1"><label htmlFor="ajMotivo" className="rotulo">Motivo</label><input id="ajMotivo" className="campo" maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} /></div>
              <button type="button" className="btn-secundario" onClick={() => void ajustar()}
                disabled={aGravar || !podeGravar || !Number.isInteger(Number(ajuste)) || Number(ajuste) === 0 || motivo.trim().length < 3}>Ajustar pontos</button>
            </div>
          )}
        </section>
      </div>
    </Modal>
  )
}

/** Resumo da conta corrente (facturas a crédito em aberto) com atalhos para o extrato e o histórico de facturas. */
function ContaCorrenteCliente({ clienteId }: { clienteId: number }) {
  const { tem } = useSessao()
  const podeVer = tem('facturacao.ver')
  const podeRelatorios = ['facturacao.ver', 'relatorios.vendas', 'relatorios.financeiros'].some(tem)
  const conta = useDados(() => (podeVer ? api.get<ContaCorrente>(`/api/documentos/conta-corrente/${clienteId}`) : Promise.resolve(null)), [clienteId, podeVer])
  if (!podeRelatorios) return null
  const c = conta.dados
  return (
    <section aria-label="Conta corrente" className="rounded-lg border border-slate-200 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-semibold">Conta corrente</h3>
          {c && (
            <p className="text-sm text-slate-600">
              {c.facturas.length === 0 ? 'Sem facturas por pagar.' : (
                <>Em dívida <span className="font-semibold tabular-nums text-slate-900">{formatarMoeda(c.totalEmDivida)}</span> em {c.facturas.length} factura(s)
                  {c.totalVencido > 0 && <> · <span className="font-medium text-red-700">vencido {formatarMoeda(c.totalVencido)}</span></>}</>
              )}
            </p>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          <Link className="btn-secundario" to={`/relatorios?tipo=contas&vista=extrato&cliente=${clienteId}`}>
            <ReceiptText className="h-4 w-4" aria-hidden /> Extrato
          </Link>
          <Link className="btn-secundario" to={`/relatorios?tipo=contas&vista=historico&cliente=${clienteId}`}>
            <FileClock className="h-4 w-4" aria-hidden /> Histórico de facturas
          </Link>
        </div>
      </div>
    </section>
  )
}
