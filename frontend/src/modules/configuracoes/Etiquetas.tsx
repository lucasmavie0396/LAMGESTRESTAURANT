import { useEffect, useState } from 'react'
import { AlertTriangle, ArrowDown, ArrowUp, CheckCircle2, Loader2, Printer } from 'lucide-react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useAtraso } from '../../hooks/useAtraso'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Configuracao, Impressora, LocalImpressoras, Pagina, Produto } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'

const CAMPOS: { id: string; rotulo: string }[] = [
  { id: 'nome', rotulo: 'Nome do produto' },
  { id: 'categoria', rotulo: 'Categoria' },
  { id: 'codigo', rotulo: 'Código interno' },
  { id: 'preco', rotulo: 'Preço' },
  { id: 'unidade', rotulo: 'Unidade (junto ao preço)' },
  { id: 'codigo-barras', rotulo: 'Código de barras (barras e número)' },
]
const URL_IMPRESSORAS = '/api/configuracoes/impressoras'
const mm = (v: string) => Number(v.replace(',', '.'))

/** Configurações → Etiquetas: tamanho, dados do produto na etiqueta, impressora e pré-visualização igual à impressão. */
export function Etiquetas() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const config = useDados(() => api.get<Configuracao[]>('/api/configuracoes'), [])
  const [largura, setLargura] = useState('')
  const [altura, setAltura] = useState('')
  const [campos, setCampos] = useState<string[]>([])
  const [aGuardar, setAGuardar] = useState(false)

  useEffect(() => {
    const v = (k: string) => config.dados?.find((c) => c.chave === k)?.valor ?? ''
    if (!config.dados) return
    setLargura(v('etiqueta.largura-mm').replace('.', ','))
    setAltura(v('etiqueta.altura-mm').replace('.', ','))
    setCampos(v('etiqueta.campos').split(',').map((c) => c.trim()).filter(Boolean))
  }, [config.dados])

  const larguraValida = mm(largura) >= 20 && mm(largura) <= 150
  const alturaValida = mm(altura) >= 10 && mm(altura) <= 150
  const valido = larguraValida && alturaValida && campos.length > 0
  const pedidoPrevisao = useAtraso(valido ? query({ largura: mm(largura), altura: mm(altura), campos: campos.join(',') }) : '', 400)

  const alternar = (id: string) => setCampos((c) => (c.includes(id) ? c.filter((x) => x !== id) : [...c, id]))
  const mover = (id: string, delta: number) => setCampos((c) => {
    const i = c.indexOf(id)
    const j = i + delta
    if (i < 0 || j < 0 || j >= c.length) return c
    const n = [...c]
    ;[n[i], n[j]] = [n[j], n[i]]
    return n
  })

  const guardar = async () => {
    setAGuardar(true)
    try {
      config.setDados(await api.put<Configuracao[]>('/api/configuracoes', {
        valores: { 'etiqueta.largura-mm': String(mm(largura)), 'etiqueta.altura-mm': String(mm(altura)), 'etiqueta.campos': campos.join(',') },
      }))
      avisos.sucesso('Etiqueta guardada.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  if (config.erro) return <Alerta tipo="erro">{config.erro}</Alerta>
  if (!config.dados) return <Carregando />

  // Os campos marcados pela ordem escolhida, seguidos dos restantes.
  const ordenados = [...campos, ...CAMPOS.map((c) => c.id).filter((id) => !campos.includes(id))]

  return (
    <div className="space-y-4">
      <section className="cartao p-4">
        <h2 className="font-semibold">Etiqueta de produto</h2>
        <p className="mb-3 max-w-3xl text-sm text-slate-600">Tamanho de cada etiqueta do rolo e os dados do produto que aparecem nela, pela ordem indicada. O código de barras usa o do produto ou, sem ele, o código interno (o leitor do POS lê os dois).</p>
        <div className="grid gap-6 lg:grid-cols-2">
          <div className="space-y-4">
            <div className="flex flex-wrap gap-3">
              <div>
                <label htmlFor="etqLargura" className="rotulo">Largura (mm)</label>
                <input id="etqLargura" className="campo w-28" inputMode="decimal" value={largura} aria-invalid={!larguraValida} onChange={(e) => setLargura(e.target.value.trim())} />
              </div>
              <div>
                <label htmlFor="etqAltura" className="rotulo">Altura (mm)</label>
                <input id="etqAltura" className="campo w-28" inputMode="decimal" value={altura} aria-invalid={!alturaValida} onChange={(e) => setAltura(e.target.value.trim())} />
              </div>
            </div>
            {(!larguraValida || !alturaValida) && <p className="text-sm text-red-700">Largura de 20 a 150 mm e altura de 10 a 150 mm.</p>}
            <fieldset>
              <legend className="rotulo">Dados na etiqueta</legend>
              <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
                {ordenados.map((id) => {
                  const c = CAMPOS.find((x) => x.id === id)!
                  const marcado = campos.includes(id)
                  return (
                    <li key={id} className="flex min-h-11 items-center gap-2 px-3">
                      <label className="flex flex-1 items-center gap-2 text-sm">
                        <input type="checkbox" className="h-4 w-4" checked={marcado} onChange={() => alternar(id)} /> {c.rotulo}
                      </label>
                      {marcado && (
                        <>
                          <button type="button" className="btn-icone h-8 w-8" aria-label={`Subir ${c.rotulo}`} onClick={() => mover(id, -1)}><ArrowUp className="h-4 w-4" /></button>
                          <button type="button" className="btn-icone h-8 w-8" aria-label={`Descer ${c.rotulo}`} onClick={() => mover(id, 1)}><ArrowDown className="h-4 w-4" /></button>
                        </>
                      )}
                    </li>
                  )
                })}
              </ul>
              {campos.length === 0 && <p className="mt-1 text-sm text-red-700">Escolha pelo menos um dado.</p>}
            </fieldset>
            <button type="button" className="btn-primario" disabled={!valido || aGuardar || !podeGravar} onClick={() => void guardar()}>
              {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar etiqueta
            </button>
          </div>
          <div>
            <p className="rotulo">Pré-visualização (produto de exemplo)</p>
            <div className="flex min-h-40 items-center justify-center rounded-lg bg-slate-100 p-4">
              {pedidoPrevisao
                ? <img src={`/api/etiquetas/previsao${pedidoPrevisao}`} alt="Pré-visualização da etiqueta" className="bg-white shadow ring-1 ring-slate-300"
                    style={{ width: `${Math.min(mm(largura) * 4, 480)}px` }} />
                : <span className="text-sm text-slate-500">Indique um tamanho válido e pelo menos um dado.</span>}
            </div>
            <p className="mt-1 text-xs text-slate-500">Desenhada pelo servidor com o mesmo motor da impressão, à resolução de uma impressora térmica (203 ppp).</p>
          </div>
        </div>
      </section>
      <ImpressoraEtiquetas />
    </div>
  )
}

/** A impressora das etiquetas: uma impressora do Windows do servidor ou de um posto (com o agente de impressão). */
function ImpressoraEtiquetas() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const impressoras = useDados(() => api.get<Impressora[]>(URL_IMPRESSORAS), [])
  const locais = useDados(() => api.get<LocalImpressoras[]>(`${URL_IMPRESSORAS}/locais`), [])
  const actual = impressoras.dados?.find((i) => i.funcao === 'Etiquetas' && i.activo) ?? impressoras.dados?.find((i) => i.funcao === 'Etiquetas')
  const [postoId, setPostoId] = useState<number | null | undefined>(undefined)
  const [nomeWindows, setNomeWindows] = useState<string | undefined>(undefined)
  const [aGuardar, setAGuardar] = useState(false)
  const [aTestar, setATestar] = useState(false)

  const posto = postoId === undefined ? actual?.postoId ?? null : postoId
  const escolhida = nomeWindows ?? actual?.nomeWindows ?? ''
  const local = locais.dados?.find((l) => l.postoId === posto)

  const guardar = async () => {
    setAGuardar(true)
    try {
      const corpo = { nome: actual?.nome ?? 'Etiquetas', funcao: 'Etiquetas', nomeWindows: escolhida, larguraPapelMm: 80, estacaoPreparacaoId: null, terminalId: null, modo: 'Directo', postoId: posto }
      if (actual) {
        await api.put(`${URL_IMPRESSORAS}/${actual.id}`, corpo)
        if (!actual.activo) await api.post(`${URL_IMPRESSORAS}/${actual.id}/activo`, { activo: true })
      } else await api.post(URL_IMPRESSORAS, corpo)
      avisos.sucesso('Impressora de etiquetas guardada.')
      setPostoId(undefined)
      setNomeWindows(undefined)
      await impressoras.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const testar = async () => {
    setATestar(true)
    try {
      const produto = (await api.get<Pagina<Produto>>('/api/produtos' + query({ activo: true, tamanho: 1 }))).itens[0]
      if (!produto) throw new Error('Crie primeiro um produto para imprimir uma etiqueta de teste.')
      const r = await api.post<{ impressora: string }>('/api/etiquetas/imprimir', { itens: [{ produtoId: produto.id, quantidade: 1 }] })
      avisos.sucesso(`Etiqueta de ${produto.nome} enviada para ${r.impressora}.`)
      window.setTimeout(() => void impressoras.recarregar(), 3000)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setATestar(false)
    }
  }

  return (
    <section className="cartao p-4">
      <h2 className="font-semibold">Impressora das etiquetas</h2>
      <p className="mb-3 text-sm text-slate-600">Impressora de etiquetas instalada no Windows do servidor ou de um posto (com o agente de impressão). Imprime directo, uma etiqueta por página do rolo.</p>
      {(impressoras.erro || locais.erro) && <Alerta tipo="erro">{impressoras.erro ?? locais.erro}</Alerta>}
      {!locais.dados && !locais.erro && <Carregando />}
      {locais.dados && (
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <label htmlFor="etqLocal" className="rotulo">Ligada a</label>
            <select id="etqLocal" className="campo min-w-48" value={posto ?? ''} onChange={(e) => { setPostoId(e.target.value ? Number(e.target.value) : null); setNomeWindows('') }}>
              {locais.dados.map((l) => (
                <option key={l.postoId ?? 'servidor'} value={l.postoId ?? ''} disabled={l.postoId !== null && !l.temAgente}>
                  {l.postoId === null ? 'Servidor' : `Posto ${l.numero} — ${l.nome}${!l.temAgente ? ' (sem agente)' : ''}`}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="etqImpressora" className="rotulo">Impressora do Windows</label>
            <select id="etqImpressora" className="campo min-w-64" value={escolhida} onChange={(e) => setNomeWindows(e.target.value)}>
              <option value="">Escolha…</option>
              {[...new Set([...(local?.impressoras ?? []), ...(escolhida ? [escolhida] : [])])].map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>
          <button type="button" className="btn-primario" disabled={!escolhida || aGuardar || !podeGravar} onClick={() => void guardar()}>
            {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar impressora
          </button>
          <button type="button" className="btn-secundario" disabled={!actual?.activo || aTestar || !podeGravar} onClick={() => void testar()}>
            {aTestar ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Printer className="h-4 w-4" aria-hidden />} Imprimir etiqueta de teste
          </button>
        </div>
      )}
      {actual && (
        <p className="mt-3 text-sm">
          {actual.ultimoErro
            ? <span className="inline-flex items-center gap-1 text-red-700"><AlertTriangle className="h-4 w-4" aria-hidden /> {actual.ultimoErro}</span>
            : actual.ultimaImpressaoUtc
              ? <span className="inline-flex items-center gap-1 text-green-700"><CheckCircle2 className="h-4 w-4" aria-hidden /> Última impressão: {formatarDataHora(actual.ultimaImpressaoUtc)}</span>
              : <span className="text-slate-500">Actual: {actual.posto ? `posto ${actual.posto}` : 'servidor'} · {actual.nomeWindows}</span>}
        </p>
      )}
    </section>
  )
}
