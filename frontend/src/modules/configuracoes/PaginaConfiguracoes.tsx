import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { BadgeCheck, CreditCard, FileCog, ImageOff, Loader2, Monitor, Plus, Printer, SlidersHorizontal, Tag, Upload } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Configuracao, ImagemGuardada, TaxaImposto } from '../../types/api'
import { DocumentosPos } from './DocumentosPos'
import { Etiquetas } from './Etiquetas'
import { Impressoras } from './Impressoras'
import { Licenca } from './Licenca'
import { Postos } from './Postos'
import { ModosPagamento } from './ModosPagamento'

const ABAS = [
  { id: 'geral', titulo: 'Geral', icone: SlidersHorizontal },
  { id: 'postos', titulo: 'Postos', icone: Monitor },
  { id: 'impressoras', titulo: 'Impressoras', icone: Printer },
  { id: 'documentos-pos', titulo: 'Documentos e POS', icone: FileCog },
  { id: 'etiquetas', titulo: 'Etiquetas', icone: Tag },
  { id: 'pagamentos', titulo: 'Modos de pagamento', icone: CreditCard },
  { id: 'licenca', titulo: 'Licença', icone: BadgeCheck },
] as const

type Campo = { chave: string; rotulo: string; tipo?: 'texto' | 'numero' | 'sim-nao' | 'longo'; ajuda?: string }

const SECCOES: { titulo: string; campos: Campo[] }[] = [
  {
    titulo: 'Estabelecimento (aparece nos recibos e documentos)',
    campos: [
      { chave: 'estabelecimento.nome', rotulo: 'Nome' },
      { chave: 'estabelecimento.nuit', rotulo: 'NUIT', ajuda: '9 algarismos' },
      { chave: 'estabelecimento.telefone', rotulo: 'Contactos' },
      { chave: 'estabelecimento.endereco', rotulo: 'Endereço', tipo: 'longo' },
      { chave: 'recibo.rodape', rotulo: 'Rodapé do recibo', tipo: 'longo' },
    ],
  },
  {
    titulo: 'Geral',
    campos: [
      { chave: 'geral.moeda', rotulo: 'Moeda (ISO)', ajuda: 'Ex.: MZN' },
      { chave: 'geral.simbolo-moeda', rotulo: 'Símbolo da moeda', ajuda: 'Ex.: MT' },
      { chave: 'geral.fuso-horario', rotulo: 'Fuso horário', ajuda: 'Ex.: Africa/Maputo' },
      { chave: 'delivery.taxa-padrao', rotulo: 'Taxa de entrega por omissão (MT)', tipo: 'numero' },
    ],
  },
  {
    titulo: 'Stock e fidelização',
    campos: [
      { chave: 'stock.permitir-negativo', rotulo: 'Permitir stock negativo em todos os artigos', tipo: 'sim-nao', ajuda: 'Desaconselhado: prefira autorizar artigo a artigo.' },
      { chave: 'fidelizacao.activa', rotulo: 'Programa de fidelização activo', tipo: 'sim-nao' },
      { chave: 'fidelizacao.valor-por-ponto', rotulo: 'Valor gasto para ganhar 1 ponto (MT)', tipo: 'numero' },
    ],
  },
]

export function PaginaConfiguracoes() {
  const { podeGravar, licenca } = useLigacao()
  const avisos = useAvisos()
  const config = useDados(() => api.get<Configuracao[]>('/api/configuracoes'), [])
  const [valores, setValores] = useState<Record<string, string>>({})
  const [aGuardar, setAGuardar] = useState(false)
  const [parametros, setParametros] = useSearchParams()

  useEffect(() => {
    if (config.dados) setValores(Object.fromEntries(config.dados.map((c) => [c.chave, c.valor])))
  }, [config.dados])

  const alterados = config.dados?.filter((c) => (valores[c.chave] ?? '') !== c.valor) ?? []

  const guardar = async () => {
    setAGuardar(true)
    try {
      // Números com vírgula são convertidos para ponto (o servidor só aceita ponto decimal).
      const corpo = Object.fromEntries(alterados.map((c) => {
        const campo = SECCOES.flatMap((s) => s.campos).find((x) => x.chave === c.chave)
        const v = valores[c.chave] ?? ''
        return [c.chave, campo?.tipo === 'numero' ? v.replace(',', '.') : v]
      }))
      const r = await api.put<Configuracao[]>('/api/configuracoes', { valores: corpo })
      config.setDados(r)
      avisos.sucesso('Configurações guardadas.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const aba = (ABAS.find((a) => a.id === parametros.get('aba')) ?? ABAS[0]).id

  return (
    <div className="mx-auto max-w-6xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Configurações</h1>
        <div className="flex gap-2">
          {aba === 'geral' && (
            <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar || alterados.length === 0}>
              {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar {alterados.length > 0 && `(${alterados.length})`}
            </button>
          )}
        </div>
      </div>

      <div role="tablist" aria-label="Secções das configurações" className="flex flex-wrap gap-1 border-b border-slate-200">
        {ABAS.map(({ id, titulo, icone: Icone }) => (
          <button key={id} type="button" role="tab" aria-selected={aba === id}
            onClick={() => {
              if (aba === 'geral' && alterados.length > 0 && !window.confirm('Há alterações por guardar nesta secção. Mudar de separador e perdê-las?')) return
              setParametros(id === 'geral' ? {} : { aba: id }, { replace: true })
            }}
            className={`-mb-px inline-flex min-h-11 items-center gap-2 border-b-2 px-4 text-sm font-medium ${aba === id ? 'border-primaria-600 text-primaria-700' : 'border-transparent text-slate-600 hover:text-slate-900'}`}>
            <Icone className="h-4 w-4" aria-hidden /> {titulo}
          </button>
        ))}
      </div>

      {aba === 'postos' && <Postos />}
      {aba === 'impressoras' && <Impressoras />}
      {aba === 'documentos-pos' && <DocumentosPos />}
      {aba === 'etiquetas' && <Etiquetas />}
      {aba === 'pagamentos' && <ModosPagamento />}
      {aba === 'licenca' && <Licenca />}
      {aba === 'geral' && config.erro && <Alerta tipo="erro">{config.erro}</Alerta>}
      {aba === 'geral' && !config.dados && !config.erro && <Carregando />}
      {aba === 'geral' && config.dados && SECCOES.map((s, indice) => (
        <section key={s.titulo} className="cartao p-4">
          <h2 className="mb-3 font-semibold">{s.titulo}</h2>
          {indice === 0 && <Logotipo inicial={config.dados?.find((c) => c.chave === 'estabelecimento.logotipo')?.valor ?? ''} />}
          <div className="grid gap-4 sm:grid-cols-2">
            {s.campos.map((c) => (
              <div key={c.chave} className={c.tipo === 'longo' ? 'sm:col-span-2' : ''}>
                {c.tipo === 'sim-nao' ? (
                  <label className="flex min-h-11 items-center gap-2 text-sm">
                    <input type="checkbox" className="h-4 w-4" checked={valores[c.chave] === 'true'} onChange={(e) => setValores((v) => ({ ...v, [c.chave]: e.target.checked ? 'true' : 'false' }))} />
                    {c.rotulo}
                  </label>
                ) : (
                  <>
                    <label htmlFor={c.chave} className="rotulo">{c.rotulo}</label>
                    {c.tipo === 'longo'
                      ? <textarea id={c.chave} rows={2} className="campo py-2" maxLength={1000} value={valores[c.chave] ?? ''} onChange={(e) => setValores((v) => ({ ...v, [c.chave]: e.target.value }))} />
                      : <input id={c.chave} className="campo" inputMode={c.tipo === 'numero' ? 'decimal' : undefined} value={valores[c.chave] ?? ''}
                          readOnly={c.chave === 'estabelecimento.nuit' && !!licenca?.nuit}
                          onChange={(e) => setValores((v) => ({ ...v, [c.chave]: e.target.value }))} />}
                  </>
                )}
                {c.chave === 'estabelecimento.nuit' && licenca?.nuit
                  ? <p className="mt-1 text-xs text-slate-500">Vem da licença: muda-se activando uma licença emitida para outro NUIT.</p>
                  : c.ajuda && <p className="mt-1 text-xs text-slate-500">{c.ajuda}</p>}
              </div>
            ))}
          </div>
        </section>
      ))}
      {aba === 'geral' && <Taxas />}
    </div>
  )
}

/**
 * Logótipo do estabelecimento: sai nos documentos A4 e nos recibos de 80 mm (servidor e navegador).
 * Grava-se logo ao carregar/retirar, sem mexer nas outras alterações por guardar desta página.
 */
function Logotipo({ inicial }: { inicial: string }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const [url, setUrl] = useState(inicial)
  const [aGuardar, setAGuardar] = useState(false)
  const ficheiro = useRef<HTMLInputElement>(null)

  const definir = async (novo: string, accao?: () => Promise<string>) => {
    setAGuardar(true)
    try {
      const valor = accao ? await accao() : novo
      await api.put('/api/configuracoes', { valores: { 'estabelecimento.logotipo': valor } })
      setUrl(valor)
      avisos.sucesso(valor ? 'Logótipo guardado.' : 'Logótipo retirado.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const carregar = (f: File) => {
    const dados = new FormData()
    dados.append('ficheiro', f)
    void definir('', async () => (await api.post<ImagemGuardada>('/api/imagens/carregar', dados)).url)
  }

  return (
    <div className="mb-4 flex flex-wrap items-center gap-4 rounded-lg border border-slate-200 p-3">
      <div className="flex h-20 w-40 items-center justify-center overflow-hidden rounded bg-white ring-1 ring-slate-200">
        {aGuardar ? <Loader2 className="h-5 w-5 animate-spin text-slate-400" aria-label="A guardar" />
          : url ? <img src={url} alt="Logótipo do estabelecimento" className="max-h-full max-w-full object-contain" />
          : <ImageOff className="h-6 w-6 text-slate-300" aria-label="Sem logótipo" />}
      </div>
      <div className="min-w-0 flex-1">
        <p className="font-medium">Logótipo</p>
        <p className="text-xs text-slate-500">Aparece nos documentos A4 e nos recibos de 80 mm. Use uma imagem com fundo branco ou transparente, recortada à volta do logótipo (PNG ou JPG).</p>
        <div className="mt-2 flex gap-2">
          <button type="button" className="btn-secundario min-h-9 text-sm" disabled={!podeGravar || aGuardar} onClick={() => ficheiro.current?.click()}>
            <Upload className="h-4 w-4" aria-hidden /> {url ? 'Substituir' : 'Carregar logótipo'}
          </button>
          {url && (
            <button type="button" className="btn-secundario min-h-9 text-sm text-red-700" disabled={!podeGravar || aGuardar} onClick={() => void definir('')}>
              Retirar
            </button>
          )}
          <input ref={ficheiro} type="file" accept="image/png,image/jpeg,image/gif,image/bmp" className="hidden" aria-label="Ficheiro do logótipo"
            onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) carregar(f) }} />
        </div>
      </div>
    </div>
  )
}

function Taxas() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const taxas = useDados(() => api.get<TaxaImposto[]>('/api/catalogo/taxas'), [])
  const [nome, setNome] = useState('')
  const [percentagem, setPercentagem] = useState('')
  const executar = async (accao: () => Promise<unknown>, ok: string) => {
    try {
      await accao()
      avisos.sucesso(ok)
      await taxas.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e))
    }
  }
  const p = Number(percentagem.replace(',', '.'))
  return (
    <section className="cartao p-4">
      <h2 className="font-semibold">Taxas de imposto</h2>
      <p className="mb-3 text-sm text-slate-600">Os preços de venda incluem o imposto. Alterar uma taxa só afecta vendas futuras. Confirme as taxas em vigor com o contabilista.</p>
      <ul className="mb-3 divide-y divide-slate-100">
        {taxas.dados?.map((t) => (
          <li key={t.id} className="flex min-h-11 items-center gap-3">
            <span className="flex-1 font-medium">{t.nome}</span>
            <span className="tabular-nums">{t.percentagem.toLocaleString('pt-MZ')}%</span>
            {!t.activo && <Etiqueta cor="cinza">Inactiva</Etiqueta>}
            <button type="button" className="text-sm font-medium text-primaria-700 hover:underline" disabled={!podeGravar}
              onClick={() => void executar(() => api.post(`/api/configuracoes/taxas/${t.id}/activo`, { activo: !t.activo }), t.activo ? 'Taxa desactivada.' : 'Taxa activada.')}>
              {t.activo ? 'Desactivar' : 'Activar'}
            </button>
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap gap-2">
        <input className="campo max-w-xs" placeholder="Nome (ex.: IVA reduzido)" maxLength={200} value={nome} onChange={(e) => setNome(e.target.value)} aria-label="Nome da taxa" />
        <input className="campo w-28" inputMode="decimal" placeholder="%" value={percentagem} onChange={(e) => setPercentagem(e.target.value)} aria-label="Percentagem" />
        <button type="button" className="btn-secundario" disabled={!podeGravar || !nome.trim() || !(p >= 0 && p <= 100)}
          onClick={() => void executar(async () => { await api.post('/api/configuracoes/taxas', { nome: nome.trim(), percentagem: p }); setNome(''); setPercentagem('') }, 'Taxa criada.')}>
          <Plus className="h-4 w-4" aria-hidden /> Adicionar
        </button>
      </div>
    </section>
  )
}

