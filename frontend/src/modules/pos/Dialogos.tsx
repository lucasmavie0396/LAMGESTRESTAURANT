import { useEffect, useState } from 'react'
import { api } from '../../api/cliente'
import { mensagemErro } from '../../hooks/useDados'
import { Loader2, Lock } from 'lucide-react'
import { Alerta, Modal } from '../../components/ui'
import type { Mesa, Pedido } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'
import { useSessao } from '../../contexts/SessaoContext'
import { mesaDeOutro } from '../../utils/mesas'
import { P } from '../../utils/permissoes'

function Botoes({ aoCancelar, aoConfirmar, texto, aProcessar, desactivado, perigo }: {
  aoCancelar: () => void
  aoConfirmar: () => void
  texto: string
  aProcessar?: boolean
  desactivado?: boolean
  perigo?: boolean
}) {
  return (
    <>
      <button type="button" className="btn-secundario" onClick={aoCancelar}>Cancelar</button>
      <button type="button" className={perigo ? 'btn-perigo' : 'btn-primario'} onClick={aoConfirmar} disabled={aProcessar || desactivado}>
        {aProcessar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} {texto}
      </button>
    </>
  )
}

/** Pede um motivo obrigatório (cancelamentos, anulações). */
export function DialogoMotivo({ titulo, descricao, texto, aoConfirmar, aoFechar }: {
  titulo: string
  descricao: string
  texto: string
  aoConfirmar: (motivo: string) => Promise<void>
  aoFechar: () => void
}) {
  const [motivo, setMotivo] = useState('')
  const [aProcessar, setAProcessar] = useState(false)
  const valido = motivo.trim().length >= 3
  const confirmar = async () => {
    setAProcessar(true)
    try {
      await aoConfirmar(motivo.trim())
    } finally {
      setAProcessar(false)
    }
  }
  return (
    <Modal titulo={titulo} aoFechar={aoFechar}
      rodape={<Botoes aoCancelar={aoFechar} aoConfirmar={() => void confirmar()} texto={texto} aProcessar={aProcessar} desactivado={!valido} perigo />}>
      <p className="mb-3 text-sm text-slate-600">{descricao}</p>
      <label htmlFor="motivo" className="rotulo">Motivo (obrigatório)</label>
      <textarea id="motivo" rows={3} className="campo py-2" maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
      {!valido && motivo.length > 0 && <p className="mt-1 text-sm text-red-600">Pelo menos 3 caracteres.</p>}
    </Modal>
  )
}

/** Passa a mesa a outro utilizador responsável (ex.: mudança de turno). */
export function DialogoPassarMesa({ pedido, aoConfirmar, aoFechar }: {
  pedido: Pedido
  aoConfirmar: (utilizadorId: number, nome: string) => Promise<void>
  aoFechar: () => void
}) {
  const [utilizadores, setUtilizadores] = useState<{ id: number; nome: string }[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [escolhido, setEscolhido] = useState<number | null>(null)
  const [aProcessar, setAProcessar] = useState(false)
  useEffect(() => {
    api.get<{ id: number; nome: string }[]>('/api/pedidos/responsaveis').then(setUtilizadores).catch((e) => setErro(mensagemErro(e)))
  }, [])
  const confirmar = async () => {
    const u = utilizadores?.find((x) => x.id === escolhido)
    if (!u) return
    setAProcessar(true)
    try {
      await aoConfirmar(u.id, u.nome)
    } finally {
      setAProcessar(false)
    }
  }
  return (
    <Modal titulo={`Passar a mesa ${pedido.mesaNome ?? ''}`} aoFechar={aoFechar}
      rodape={<Botoes aoCancelar={aoFechar} aoConfirmar={() => void confirmar()} texto="Passar mesa" aProcessar={aProcessar} desactivado={!escolhido} />}>
      <div className="space-y-3">
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
        <p className="text-sm text-slate-600">
          Responsável actual: <strong>{pedido.responsavel ?? '—'}</strong>. O novo responsável passa a ver e lançar nesta mesa; fica registado na auditoria.
        </p>
        <div>
          <label htmlFor="novoResponsavel" className="rotulo">Novo responsável</label>
          <select id="novoResponsavel" className="campo" value={escolhido ?? ''} onChange={(e) => setEscolhido(Number(e.target.value) || null)}>
            <option value="">Escolha…</option>
            {utilizadores?.filter((u) => u.id !== pedido.responsavelId).map((u) => <option key={u.id} value={u.id}>{u.nome}</option>)}
          </select>
        </div>
      </div>
    </Modal>
  )
}

/** Quantidade ao lançar um produto (teclado no ecrã para postos tácteis). */
export function DialogoQuantidade({ produto, unidade, aoConfirmar, aoFechar }: {
  produto: string
  unidade: string
  aoConfirmar: (quantidade: number) => void
  aoFechar: () => void
}) {
  const [texto, setTexto] = useState('')
  const valor = Number(texto.replace(',', '.'))
  const valido = texto !== '' && valor > 0 && valor <= 999 && Math.abs(valor * 1000 - Math.round(valor * 1000)) < 1e-6
  const tecla = (t: string) => setTexto((x) => (t === ',' && x.includes(',') ? x : x.length < 8 ? x + t : x))
  return (
    <Modal titulo={produto} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={!valido} onClick={() => aoConfirmar(valor)}>Lançar</button>
      </>}>
      <form onSubmit={(e) => { e.preventDefault(); if (valido) aoConfirmar(valor) }} className="mx-auto max-w-xs space-y-3">
        <div>
          <label htmlFor="quantidadePos" className="rotulo">Quantidade ({unidade})</label>
          <input id="quantidadePos" autoFocus inputMode="decimal" className="campo text-right text-xl tabular-nums" value={texto} aria-invalid={texto !== '' && !valido}
            onChange={(e) => setTexto(e.target.value.trim().replace('.', ','))} />
        </div>
        <div className="grid grid-cols-3 gap-2">
          {['7', '8', '9', '4', '5', '6', '1', '2', '3', ',', '0'].map((t) => (
            <button key={t} type="button" className="btn-secundario min-h-12 text-lg" onClick={() => tecla(t)}>{t}</button>
          ))}
          <button type="button" className="btn-secundario min-h-12" aria-label="Apagar" onClick={() => setTexto((x) => x.slice(0, -1))}>←</button>
        </div>
      </form>
    </Modal>
  )
}

/**
 * Comentário livre de um produto (ex.: «sem batata») ou do pedido inteiro, com sugestões rápidas (Configurações → Documentos e POS).
 * `aviso`: ex.: o produto já foi enviado e sai um talão de alteração.
 */
export function DialogoObservacoes({ inicial, produto, sugestoes = [], aviso, aoConfirmar, aoFechar }: {
  inicial: string
  produto: string
  sugestoes?: string[]
  aviso?: string
  aoConfirmar: (texto: string) => Promise<void>
  aoFechar: () => void
}) {
  const [texto, setTexto] = useState(inicial)
  const [aProcessar, setAProcessar] = useState(false)
  return (
    <Modal titulo={`Comentário: ${produto}`} aoFechar={aoFechar}
      rodape={<Botoes aoCancelar={aoFechar} texto="Guardar" aProcessar={aProcessar}
        aoConfirmar={() => { setAProcessar(true); void aoConfirmar(texto.trim()).finally(() => setAProcessar(false)) }} />}>
      {aviso && <div className="mb-3"><Alerta tipo="info">{aviso}</Alerta></div>}
      {sugestoes.length > 0 && (
        <div className="mb-3 flex flex-wrap gap-2">
          {sugestoes.map((s) => (
            <button key={s} type="button" className="btn-secundario min-h-11 px-3 text-sm" onClick={() => setTexto((t) => (t ? `${t}, ${s}` : s))}>{s}</button>
          ))}
        </div>
      )}
      <label htmlFor="comentario" className="rotulo">Comentário livre</label>
      <textarea id="comentario" rows={3} className="campo py-2" maxLength={300} value={texto} autoFocus onChange={(e) => setTexto(e.target.value)}
        placeholder="Ex.: sem batata" />
      {texto && (
        <button type="button" className="mt-2 text-sm font-medium text-red-700 hover:underline" onClick={() => setTexto('')}>Apagar comentário</button>
      )}
    </Modal>
  )
}

export function DialogoDesconto({ pedido, aoConfirmar, aoFechar }: {
  pedido: Pedido
  aoConfirmar: (dados: { valor: number | null; percentagem: number | null }) => Promise<void>
  aoFechar: () => void
}) {
  const [modo, setModo] = useState<'percentagem' | 'valor'>('percentagem')
  const [entrada, setEntrada] = useState('')
  const [aProcessar, setAProcessar] = useState(false)
  const numero = Number(entrada)
  const valido = entrada !== '' && Number.isFinite(numero) && numero >= 0 && (modo === 'percentagem' ? numero <= 100 : numero <= pedido.subtotal)
  const previsto = !valido ? null : modo === 'percentagem' ? Math.round(pedido.subtotal * numero) / 100 : numero

  return (
    <Modal titulo="Desconto no pedido" aoFechar={aoFechar}
      rodape={<Botoes aoCancelar={aoFechar} texto="Aplicar" aProcessar={aProcessar} desactivado={!valido}
        aoConfirmar={() => {
          setAProcessar(true)
          void aoConfirmar(modo === 'percentagem' ? { valor: null, percentagem: numero } : { valor: numero, percentagem: null }).finally(() => setAProcessar(false))
        }} />}>
      <div className="mb-3 flex gap-2">
        {(['percentagem', 'valor'] as const).map((m) => (
          <button key={m} type="button" onClick={() => setModo(m)} className={modo === m ? 'btn-primario flex-1' : 'btn-secundario flex-1'}>
            {m === 'percentagem' ? 'Percentagem (%)' : 'Valor (MT)'}
          </button>
        ))}
      </div>
      <input type="number" inputMode="decimal" min={0} step="0.01" className="campo text-lg" value={entrada} onChange={(e) => setEntrada(e.target.value)} aria-label="Desconto" />
      <p className="mt-2 text-sm text-slate-600">
        Subtotal: {formatarMoeda(pedido.subtotal)}
        {previsto !== null && <> · Desconto: <strong>{formatarMoeda(previsto)}</strong> (calculado no servidor)</>}
      </p>
      <p className="mt-1 text-xs text-slate-500">Use 0 para retirar o desconto. A operação fica registada na auditoria.</p>
    </Modal>
  )
}

export function DialogoMesa({ titulo, mesas, aoEscolher, aoFechar, apenasLivres }: {
  titulo: string
  mesas: Mesa[]
  aoEscolher: (mesa: Mesa) => void
  aoFechar: () => void
  apenasLivres?: boolean
}) {
  return (
    <Modal titulo={titulo} aoFechar={aoFechar} largura="max-w-3xl">
      <GrelhaMesas mesas={mesas} aoEscolher={aoEscolher} apenasLivres={apenasLivres} />
    </Modal>
  )
}

/** Mesas agrupadas por zona, com a cor do estado (livre, ocupada, reservada, indisponível). */
export function GrelhaMesas({ mesas, aoEscolher, apenasLivres, desactivado }: {
  mesas: Mesa[]
  aoEscolher: (mesa: Mesa) => void
  apenasLivres?: boolean
  desactivado?: boolean
}) {
  const cores: Record<Mesa['estado'], string> = {
    Livre: 'border-green-300 bg-green-50',
    Ocupada: 'border-red-300 bg-red-50',
    Reservada: 'border-amber-300 bg-amber-50',
    Indisponivel: 'border-slate-300 bg-slate-100 opacity-50',
  }
  const { tem, utilizador } = useSessao()
  const zonas = [...new Set(mesas.map((m) => m.zonaNome))]
  return (
    <>
      {mesas.length === 0 && <Alerta tipo="info">Não há mesas configuradas.</Alerta>}
      {zonas.map((z) => (
        <section key={z} className="mb-4">
          <h3 className="mb-2 text-sm font-semibold text-slate-600">{z}</h3>
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-5">
            {mesas.filter((m) => m.zonaNome === z).map((m) => {
              const desactivada = desactivado || m.estado === 'Indisponivel' || (apenasLivres && m.estado !== 'Livre')
              const deOutro = mesaDeOutro(m, utilizador?.id, tem(P.posMesasOutros))
              return (
                <button key={m.id} type="button" disabled={desactivada} onClick={() => aoEscolher(m)}
                  className={`flex min-h-20 flex-col items-center justify-center rounded-xl border-2 px-1 ${cores[m.estado]} disabled:cursor-not-allowed`}>
                  <span className="text-lg font-bold">{m.nome}</span>
                  <span className="text-xs">{m.estado === 'Ocupada' ? (deOutro ? 'Ocupada' : formatarMoeda(m.totalAberto)) : m.estado === 'Indisponivel' ? 'Indisponível' : m.estado}</span>
                  {m.responsavel && (
                    <span className="flex max-w-full items-center gap-1 truncate text-[11px] text-slate-600">
                      {deOutro && <Lock className="h-3 w-3 shrink-0" aria-hidden />}{m.responsavel}
                    </span>
                  )}
                </button>
              )
            })}
          </div>
        </section>
      ))}
    </>
  )
}

export function DialogoEntrega({ tipo, aoConfirmar, aoFechar }: {
  tipo: 'TakeAway' | 'Delivery'
  aoConfirmar: (dados: { nome: string; contacto: string; endereco: string }) => void
  aoFechar: () => void
}) {
  const [nome, setNome] = useState('')
  const [contacto, setContacto] = useState('')
  const [endereco, setEndereco] = useState('')
  const valido = tipo === 'TakeAway' || (nome.trim().length > 0 && contacto.trim().length > 0 && endereco.trim().length > 0)
  return (
    <Modal titulo={tipo === 'Delivery' ? 'Novo pedido Delivery' : 'Novo pedido Take Away'} aoFechar={aoFechar}
      rodape={<Botoes aoCancelar={aoFechar} texto="Continuar" desactivado={!valido}
        aoConfirmar={() => aoConfirmar({ nome: nome.trim(), contacto: contacto.trim(), endereco: endereco.trim() })} />}>
      <div className="space-y-4">
        <p className="text-sm text-slate-600">O {tipo === 'Delivery' ? 'nome, o contacto e o endereço saem' : 'nome e o contacto saem'} no recibo de pagamento.</p>
        <div>
          <label htmlFor="nomeEntrega" className="rotulo">Nome do cliente {tipo === 'Delivery' ? '(obrigatório)' : '(opcional)'}</label>
          <input id="nomeEntrega" className="campo" maxLength={150} value={nome} onChange={(e) => setNome(e.target.value)} autoComplete="off" />
        </div>
        <div>
          <label htmlFor="contactoEntrega" className="rotulo">Contacto {tipo === 'Delivery' ? '(obrigatório)' : '(opcional)'}</label>
          <input id="contactoEntrega" type="tel" className="campo" maxLength={200} value={contacto} onChange={(e) => setContacto(e.target.value)} placeholder="84 000 0000" />
        </div>
        {tipo === 'Delivery' && (
          <div>
            <label htmlFor="enderecoEntrega" className="rotulo">Endereço de entrega (obrigatório)</label>
            <textarea id="enderecoEntrega" rows={2} className="campo py-2" maxLength={1000} value={endereco} onChange={(e) => setEndereco(e.target.value)} />
            <p className="mt-1 text-xs text-slate-500">A taxa de entrega configurada é acrescentada pelo servidor.</p>
          </div>
        )}
      </div>
    </Modal>
  )
}
