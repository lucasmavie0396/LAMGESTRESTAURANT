import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Configuracao } from '../../types/api'

interface Opcao { chave: string; rotulo: string; ajuda?: string }

const FECHO: Opcao[] = [
  { chave: 'impressao.fecho-produtos', rotulo: 'Imprimir os produtos vendidos', ajuda: 'Lista por categoria, com quantidades e valores, no fim do documento do fecho.' },
  { chave: 'impressao.fecho-contagem-cega', rotulo: 'Imprimir contagem cega', ajuda: 'O documento do fecho mostra só o valor contado, sem recebimentos, valor esperado nem diferença.' },
  { chave: 'caixa.exigir-dia', rotulo: 'Exigir «Abrir Dia» para abrir os caixas', ajuda: 'Menu Caixa: o gerente abre o dia antes dos caixas e, no fim, «Fechar Dia» junta todos os caixas num só fecho.' },
]
const LOGOTIPO: Opcao[] = [
  { chave: 'impressao.logotipo-80mm', rotulo: 'Imprimir o logótipo nos documentos de 80 mm', ajuda: 'Os papéis de 58 mm e A4 mantêm o logótipo.' },
]
const MODOS_POS: Opcao[] = [
  { chave: 'pos.mesa', rotulo: 'Mesa' },
  { chave: 'pos.take-away', rotulo: 'Take Away' },
  { chave: 'pos.delivery', rotulo: 'Delivery' },
  { chave: 'pos.balcao', rotulo: 'Balcão (venda rápida)', ajuda: 'Lança e paga sem escolher mesa; os talões da cozinha e do bar saem ao pagar.' },
]
const LEITOR: Opcao[] = [
  { chave: 'pos.codigo-barras', rotulo: 'Usar leitor de código de barras', ajuda: 'Basta passar o produto no leitor no ecrã POS. Sem venda iniciada, começa uma venda ao Balcão.' },
  { chave: 'pos.mostrar-stock', rotulo: 'Mostrar o stock nos botões dos produtos', ajuda: 'Só nos produtos que controlam stock; laranja no mínimo ou abaixo, vermelho a zero. Actualiza-se a cada venda.' },
]
const SUGESTOES = 'pos.sugestoes-comentario'
const COPIAS: { chave: string; rotulo: string }[] = [
  { chave: 'talao', rotulo: 'Talão de pedido (cozinha/bar)' },
  { chave: 'anulacao', rotulo: 'Talão de anulação' },
  { chave: 'conta', rotulo: 'Conta' },
  { chave: 'venda-dinheiro', rotulo: 'Venda a dinheiro' },
  { chave: 'factura', rotulo: 'Factura' },
  { chave: 'recibo', rotulo: 'Recibo' },
  { chave: 'cotacao', rotulo: 'Cotação' },
  { chave: 'nota-credito', rotulo: 'Nota de crédito' },
  { chave: 'fecho', rotulo: 'Fecho de caixa' },
  { chave: 'relatorios', rotulo: 'Relatórios' },
].map((c) => ({ ...c, chave: `impressao.copias.${c.chave}` }))

const copiasValidas = (v: string | undefined) => /^\d{1,2}$/.test(v ?? '') && Number(v) >= 1 && Number(v) <= 10

/** Configurações → Documentos e POS: o que sai nos documentos, quantas cópias e o que aparece no ecrã POS. */
export function DocumentosPos() {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const config = useDados(() => api.get<Configuracao[]>('/api/configuracoes'), [])
  const [valores, setValores] = useState<Record<string, string>>({})
  const [aGuardar, setAGuardar] = useState(false)

  useEffect(() => {
    if (config.dados) setValores(Object.fromEntries(config.dados.map((c) => [c.chave, c.valor])))
  }, [config.dados])

  if (config.erro) return <Alerta tipo="erro">{config.erro}</Alerta>
  if (!config.dados) return <Carregando />

  const definir = (chave: string, valor: string) => setValores((v) => ({ ...v, [chave]: valor }))
  const nossas = [...[...FECHO, ...LOGOTIPO, ...MODOS_POS, ...LEITOR, ...COPIAS].map((o) => o.chave), SUGESTOES]
  const alterados = config.dados.filter((c) => nossas.includes(c.chave) && valores[c.chave] !== undefined && valores[c.chave] !== c.valor)
  const semModos = MODOS_POS.every((m) => valores[m.chave] === 'false')
  const copiasErradas = COPIAS.some((c) => !copiasValidas(valores[c.chave]))

  const guardar = async () => {
    setAGuardar(true)
    try {
      config.setDados(await api.put<Configuracao[]>('/api/configuracoes', { valores: Object.fromEntries(alterados.map((c) => [c.chave, valores[c.chave]])) }))
      avisos.sucesso('Configurações guardadas.')
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  const caixas = (opcoes: Opcao[]) => opcoes.map((o) => (
    <div key={o.chave}>
      <label className="flex min-h-11 items-center gap-2 text-sm">
        <input type="checkbox" className="h-4 w-4" checked={valores[o.chave] !== 'false'} onChange={(e) => definir(o.chave, e.target.checked ? 'true' : 'false')} />
        {o.rotulo}
      </label>
      {o.ajuda && <p className="text-xs text-slate-500">{o.ajuda}</p>}
    </div>
  ))

  return (
    <div className="space-y-4">
      <section className="cartao p-4">
        <h2 className="mb-2 font-semibold">Fecho de caixa impresso</h2>
        <div className="space-y-2">
          {FECHO.map((o) => (
            <div key={o.chave}>
              <label className="flex min-h-11 items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" checked={valores[o.chave] === 'true'} onChange={(e) => definir(o.chave, e.target.checked ? 'true' : 'false')} />
                {o.rotulo}
              </label>
              {o.ajuda && <p className="text-xs text-slate-500">{o.ajuda}</p>}
            </div>
          ))}
        </div>
      </section>

      <section className="cartao p-4">
        <h2 className="mb-2 font-semibold">Logótipo</h2>
        {caixas(LOGOTIPO)}
      </section>

      <section className="cartao p-4">
        <h2 className="font-semibold">Número de cópias por documento</h2>
        <p className="mb-3 text-sm text-slate-600">De 1 a 10. Vale para as impressoras do servidor, dos postos e para a impressão pelo navegador. Em impressoras no modo PDF, o documento abre uma vez.</p>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {COPIAS.map((c) => (
            <div key={c.chave} className="flex items-center justify-between gap-3">
              <label htmlFor={c.chave} className="text-sm">{c.rotulo}</label>
              <input id={c.chave} className="campo w-20 text-center" inputMode="numeric" maxLength={2} aria-invalid={!copiasValidas(valores[c.chave])}
                value={valores[c.chave] ?? ''} onChange={(e) => definir(c.chave, e.target.value.trim())} />
            </div>
          ))}
        </div>
        {copiasErradas && <p className="mt-2 text-sm text-red-700">O número de cópias é de 1 a 10.</p>}
      </section>

      <section className="cartao p-4">
        <h2 className="font-semibold">Ecrã POS</h2>
        <p className="mb-2 text-sm text-slate-600">Botões disponíveis para iniciar uma venda. Os pedidos já abertos continuam a funcionar.</p>
        <div className="grid gap-2 sm:grid-cols-2">{caixas(MODOS_POS)}</div>
        {semModos && <p className="mt-2 text-sm text-red-700">Deixe pelo menos um botão activo.</p>}
        <div className="mt-3 border-t border-slate-100 pt-2">{caixas(LEITOR)}</div>
        <div className="mt-3 border-t border-slate-100 pt-3">
          <label htmlFor="sugestoesComentario" className="rotulo">Sugestões de comentários dos produtos</label>
          <textarea id="sugestoesComentario" rows={4} className="campo py-2" maxLength={1000} value={valores[SUGESTOES] ?? ''}
            placeholder={'Sem batata\nMolho à parte'} onChange={(e) => definir(SUGESTOES, e.target.value)} />
          <p className="text-xs text-slate-500">Uma por linha (até 60 caracteres). Aparecem como botões ao comentar um produto no POS; o comentário livre continua disponível.</p>
        </div>
      </section>

      <div className="flex justify-end">
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar || alterados.length === 0 || semModos || copiasErradas}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar {alterados.length > 0 && `(${alterados.length})`}
        </button>
      </div>
    </div>
  )
}
