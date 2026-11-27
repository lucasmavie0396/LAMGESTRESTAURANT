import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { Configuracao, TabelaPreco } from '../../types/api'

interface Opcao { chave: string; rotulo: string; ajuda?: string }

const FECHO: Opcao[] = [
  { chave: 'impressao.fecho-automatico', rotulo: 'Imprimir o fecho automaticamente ao fechar', ajuda: 'O fecho de caixa e o fecho do dia saem logo na impressora de caixa do terminal (sem ela, abre a janela de impressão). O botão «Imprimir» continua para segundas vias.' },
  { chave: 'impressao.fecho-produtos', rotulo: 'Imprimir os produtos vendidos', ajuda: 'Lista por categoria, com quantidades e valores, no fim do documento do fecho.' },
  { chave: 'impressao.fecho-contagem-cega', rotulo: 'Imprimir contagem cega', ajuda: 'O documento do fecho mostra só o valor contado, sem recebimentos, valor esperado nem diferença.' },
  { chave: 'caixa.exigir-dia', rotulo: 'Exigir «Abrir Dia» para abrir os caixas', ajuda: 'Menu Caixa: o gerente abre o dia antes dos caixas e, no fim, «Fechar Dia» junta todos os caixas num só fecho.' },
  { chave: 'caixa.fechar-dia-contas-abertas', rotulo: 'Permitir fechar o dia com contas abertas', ajuda: 'Ligado: as contas ainda abertas transitam para o dia seguinte e saem listadas no fecho. Desligado: «Fechar Dia» só é possível depois de pagar ou cancelar todas as contas.' },
]
const LOGOTIPO: Opcao[] = [
  { chave: 'impressao.logotipo-80mm', rotulo: 'Imprimir o logótipo nos documentos de 80 mm', ajuda: 'Os papéis de 58 mm e A4 mantêm o logótipo.' },
]
const CONTA_RECIBO: Opcao[] = [
  { chave: 'impressao.conta-dados-factura', rotulo: 'Imprimir os dados para factura no pedido de conta', ajuda: 'No fim da conta, linhas em branco para o cliente escrever o nome comercial e o NUIT da empresa, se quiser factura.' },
  { chave: 'impressao.publicidade-provedor', rotulo: 'Imprimir o provedor do sistema na conta e no recibo', ajuda: 'Uma linha pequena no fim dos talões de 58 e 80 mm: «Provedor do sistema: LAM Company» e os contactos. Em A4 não sai.' },
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
/** Tabela de preço do botão «Retalho» do balcão (vazio = sem botão). */
const RETALHO = 'pos.tabela-retalho'
const BALANCA = {
  activa: 'balanca.etiquetas', prefixos: 'balanca.etiqueta-prefixos', digitos: 'balanca.etiqueta-digitos-codigo',
  conteudo: 'balanca.etiqueta-conteudo', decimais: 'balanca.etiqueta-decimais-preco',
}
const prefixosValidos = (v: string | undefined) => {
  const partes = (v ?? '').split(',').map((p) => p.trim()).filter(Boolean)
  return partes.length > 0 && partes.length <= 10 && partes.every((p) => /^2\d?$/.test(p))
}

/** Exemplo de etiqueta com o formato escolhido: 2 algarismos de prefixo, o código do produto, o peso ou o preço e o controlo. */
function exemploEtiqueta(valores: Record<string, string>) {
  const prefixo = ((valores[BALANCA.prefixos] ?? '2').split(',')[0]?.trim() || '2').padEnd(2, '1').slice(0, 2)
  const digitos = Number(valores[BALANCA.digitos] ?? '5')
  const preco = valores[BALANCA.conteudo] === 'preco'
  const decimais = Number(valores[BALANCA.decimais] ?? '0')
  const resto = 10 - digitos
  const codigo = '123'.padStart(digitos, '0')
  const valor = preco ? String(Math.round(606.25 * 10 ** decimais)).padStart(resto, '0').slice(-resto) : '485'.padStart(resto, '0')
  const significado = preco ? `preço ${(Number(valor) / 10 ** decimais).toLocaleString('pt-PT', { minimumFractionDigits: decimais })}` : 'peso 0,485 kg'
  return { partes: [prefixo, codigo, valor, 'C'], texto: `produto 123, ${significado}` }
}
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
  const tabelas = useDados(() => api.get<TabelaPreco[]>('/api/tabelas-preco'), [])
  const [valores, setValores] = useState<Record<string, string>>({})
  const [aGuardar, setAGuardar] = useState(false)

  useEffect(() => {
    if (config.dados) setValores(Object.fromEntries(config.dados.map((c) => [c.chave, c.valor])))
  }, [config.dados])

  if (config.erro) return <Alerta tipo="erro">{config.erro}</Alerta>
  if (!config.dados) return <Carregando />

  const definir = (chave: string, valor: string) => setValores((v) => ({ ...v, [chave]: valor }))
  const nossas = [...[...FECHO, ...LOGOTIPO, ...CONTA_RECIBO, ...MODOS_POS, ...LEITOR, ...COPIAS].map((o) => o.chave), SUGESTOES, RETALHO, ...Object.values(BALANCA)]
  const alterados = config.dados.filter((c) => nossas.includes(c.chave) && valores[c.chave] !== undefined && valores[c.chave] !== c.valor)
  const semModos = MODOS_POS.every((m) => valores[m.chave] === 'false')
  const copiasErradas = COPIAS.some((c) => !copiasValidas(valores[c.chave]))
  const prefixosErrados = valores[BALANCA.activa] === 'true' && !prefixosValidos(valores[BALANCA.prefixos])
  const exemplo = exemploEtiqueta(valores)

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
        <h2 className="mb-2 font-semibold">Conta e recibo</h2>
        {caixas(CONTA_RECIBO)}
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
        <div className="mt-4 max-w-md">
          <label htmlFor="tabelaRetalho" className="rotulo">Botão «Normal / Retalho» no balcão</label>
          <select id="tabelaRetalho" className="campo" value={valores[RETALHO] ?? ''} onChange={(e) => definir(RETALHO, e.target.value)}>
            <option value="">Sem botão (só o preço normal)</option>
            {tabelas.dados?.map((t) => <option key={t.id} value={t.id}>Retalho = tabela «{t.nome}»</option>)}
          </select>
          <p className="mt-1 text-xs text-slate-500">Na venda rápida, o botão muda os preços para esta tabela (Produtos → Tabelas de preço). Só quem tem a permissão «Mudar o balcão para o preço de retalho».</p>
        </div>
        <div className="mt-3 border-t border-slate-100 pt-2">{caixas(LEITOR)}</div>
        <div className="mt-3 border-t border-slate-100 pt-3">
          <label htmlFor="sugestoesComentario" className="rotulo">Sugestões de comentários dos produtos</label>
          <textarea id="sugestoesComentario" rows={4} className="campo py-2" maxLength={1000} value={valores[SUGESTOES] ?? ''}
            placeholder={'Sem batata\nMolho à parte'} onChange={(e) => definir(SUGESTOES, e.target.value)} />
          <p className="text-xs text-slate-500">Uma por linha (até 60 caracteres). Aparecem como botões ao comentar um produto no POS; o comentário livre continua disponível.</p>
        </div>
      </section>

      <section className="cartao p-4" aria-label="Etiquetas da balança">
        <h2 className="font-semibold">Etiquetas da balança</h2>
        <p className="mb-2 text-sm text-slate-600">
          Balanças que imprimem uma etiqueta com código de barras (EAN-13) com o peso ou o preço. Ao ler a etiqueta no POS, o produto com
          esse «Código na balança» entra com a quantidade certa. A balança ligada ao posto configura-se em Postos → «Balança».
        </p>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" checked={valores[BALANCA.activa] === 'true'} onChange={(e) => definir(BALANCA.activa, e.target.checked ? 'true' : 'false')} />
          Ler as etiquetas da balança no POS
        </label>
        {valores[BALANCA.activa] === 'true' && (
          <div className="mt-2 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div>
              <label htmlFor="balancaPrefixos" className="rotulo">Começa por</label>
              <input id="balancaPrefixos" className="campo font-mono" maxLength={40} aria-invalid={prefixosErrados} value={valores[BALANCA.prefixos] ?? ''}
                onChange={(e) => definir(BALANCA.prefixos, e.target.value.replace(/\s/g, ''))} />
              <p className="mt-1 text-xs text-slate-500">«2» = de 20 a 29; ou só alguns: 21,28</p>
            </div>
            <div>
              <label htmlFor="balancaDigitos" className="rotulo">Algarismos do código do produto</label>
              <select id="balancaDigitos" className="campo" value={valores[BALANCA.digitos] ?? '5'} onChange={(e) => definir(BALANCA.digitos, e.target.value)}>
                {['4', '5', '6'].map((d) => <option key={d} value={d}>{d}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="balancaConteudo" className="rotulo">A etiqueta leva</label>
              <select id="balancaConteudo" className="campo" value={valores[BALANCA.conteudo] ?? 'peso'} onChange={(e) => definir(BALANCA.conteudo, e.target.value)}>
                <option value="peso">O peso (em gramas)</option>
                <option value="preco">O preço</option>
              </select>
            </div>
            {valores[BALANCA.conteudo] === 'preco' && (
              <div>
                <label htmlFor="balancaDecimais" className="rotulo">Casas decimais do preço</label>
                <select id="balancaDecimais" className="campo" value={valores[BALANCA.decimais] ?? '0'} onChange={(e) => definir(BALANCA.decimais, e.target.value)}>
                  {['0', '1', '2'].map((d) => <option key={d} value={d}>{d}</option>)}
                </select>
              </div>
            )}
            <div className="sm:col-span-2 lg:col-span-4">
              <p className="text-sm">
                Exemplo: <code className="rounded bg-slate-100 px-1.5 py-0.5" data-testid="exemplo-etiqueta">{exemplo.partes.join(' ')}</code> → {exemplo.texto} (C = algarismo de controlo).
              </p>
              {valores[BALANCA.conteudo] === 'preco' && (
                <p className="mt-1 text-xs text-amber-700">Com o preço na etiqueta, o peso é calculado a dividir pelo preço por kg: o total pode diferir uns centavos. Prefira etiquetas com o peso.</p>
              )}
              {prefixosErrados && <p className="mt-1 text-sm text-red-700">Prefixos de 1 ou 2 algarismos começados por 2, separados por vírgulas.</p>}
            </div>
          </div>
        )}
      </section>

      <div className="flex justify-end">
        <button type="button" className="btn-primario" onClick={() => void guardar()} disabled={aGuardar || !podeGravar || alterados.length === 0 || semModos || copiasErradas || prefixosErrados}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar {alterados.length > 0 && `(${alterados.length})`}
        </button>
      </div>
    </div>
  )
}
