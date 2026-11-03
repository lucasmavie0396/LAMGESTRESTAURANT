import { useEffect, useState } from 'react'
import { Loader2, Plus } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Configuracao, MetodoPagamento, TaxaImposto } from '../../types/api'
import { GestaoTerminais } from '../caixa/GestaoTerminais'

type Campo = { chave: string; rotulo: string; tipo?: 'texto' | 'numero' | 'sim-nao' | 'longo'; ajuda?: string }

const SECCOES: { titulo: string; campos: Campo[] }[] = [
  {
    titulo: 'Estabelecimento (aparece no recibo)',
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
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const config = useDados(() => api.get<Configuracao[]>('/api/configuracoes'), [])
  const [valores, setValores] = useState<Record<string, string>>({})
  const [aGuardar, setAGuardar] = useState(false)
  const [terminais, setTerminais] = useState(false)

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

  return (
    <div className="mx-auto max-w-5xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Configurações</h1>
        <div className="flex gap-2">
          <button type="button" className="btn-secundario" onClick={() => setTerminais(true)}>Terminais de caixa</button>
          <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar || alterados.length === 0}>
            {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar {alterados.length > 0 && `(${alterados.length})`}
          </button>
        </div>
      </div>
      {config.erro && <Alerta tipo="erro">{config.erro}</Alerta>}
      {!config.dados && !config.erro && <Carregando />}
      {config.dados && SECCOES.map((s) => (
        <section key={s.titulo} className="cartao p-4">
          <h2 className="mb-3 font-semibold">{s.titulo}</h2>
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
                      : <input id={c.chave} className="campo" inputMode={c.tipo === 'numero' ? 'decimal' : undefined} value={valores[c.chave] ?? ''} onChange={(e) => setValores((v) => ({ ...v, [c.chave]: e.target.value }))} />}
                  </>
                )}
                {c.ajuda && <p className="mt-1 text-xs text-slate-500">{c.ajuda}</p>}
              </div>
            ))}
          </div>
        </section>
      ))}
      <Taxas />
      <Metodos />
      {terminais && <GestaoTerminais aoFechar={() => setTerminais(false)} />}
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

function Metodos() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const metodos = useDados(() => api.get<MetodoPagamento[]>('/api/caixa/metodos-pagamento'), [])
  return (
    <section className="cartao p-4">
      <h2 className="mb-3 font-semibold">Métodos de pagamento</h2>
      <ul className="divide-y divide-slate-100">
        {metodos.dados?.map((m) => (
          <li key={m.id} className="flex min-h-11 items-center gap-3">
            <span className="flex-1 font-medium">{m.nome}</span>
            <span className="text-xs text-slate-500">{[m.permiteTroco && 'dá troco', m.exigeReferencia && 'exige referência', m.contaNaGaveta && 'conta na gaveta'].filter(Boolean).join(' · ')}</span>
            {!m.activo && <Etiqueta cor="cinza">Inactivo</Etiqueta>}
            <button type="button" className="text-sm font-medium text-primaria-700 hover:underline" disabled={!podeGravar}
              onClick={async () => {
                try {
                  await api.post(`/api/configuracoes/metodos-pagamento/${m.id}/activo`, { activo: !m.activo })
                  avisos.sucesso(m.activo ? 'Método desactivado.' : 'Método activado.')
                  await metodos.recarregar()
                } catch (e) {
                  avisos.erro(mensagemErro(e))
                }
              }}>
              {m.activo ? 'Desactivar' : 'Activar'}
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
