import { useState } from 'react'
import type { ReactNode } from 'react'
import { api, query } from '../../api/cliente'
import { Alerta, Carregando } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type {
  RelatorioContasPagar, RelatorioCotacoes, RelatorioCozinha, RelatorioDelivery, RelatorioHoras, RelatorioMesas, RelatorioPontos, RelatorioReservas,
  SituacaoCotacao, SituacaoReserva,
} from '../../types/api'
import { dataCurta } from '../../utils/datas'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { Indicadores, Metodologia, Seccao } from './comum'
import { tabela } from './formato'

type Celula = string | number | null | undefined

/** Tabela simples dos relatórios: as colunas indicadas em `direita` alinham à direita (valores). */
function Tabela({ colunas, linhas, direita = [], alerta }: { colunas: string[]; linhas: ReactNode[][]; direita?: number[]; alerta?: (i: number) => boolean }) {
  if (linhas.length === 0) return <p className="text-sm text-slate-500">Nenhum no período.</p>
  return (
    <div className="max-h-[32rem] overflow-auto">
      <table className="w-full text-left text-sm">
        <thead className="sticky top-0 bg-superficie text-slate-600"><tr className="border-b border-slate-200">
          {colunas.map((c, i) => <th key={c} scope="col" className={`py-2 pr-2 ${direita.includes(i) ? 'text-right' : ''}`}>{c}</th>)}
        </tr></thead>
        <tbody className="divide-y divide-slate-100 tabular-nums">
          {linhas.map((l, i) => (
            <tr key={i} className={alerta?.(i) ? 'text-red-700' : ''}>
              {l.map((c, j) => <td key={j} className={`py-1.5 pr-2 ${direita.includes(j) ? 'text-right' : ''}`}>{c}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function useRelatorio<T>(caminho: string, parametros: Record<string, string | number | null | undefined>, deps: unknown[]) {
  return useDados(() => api.get<T>(`/api/relatorios/${caminho}` + query(parametros)), deps)
}

/** Erro e espera comuns às vistas. */
function Estado({ erro }: { erro: string | null }) {
  return erro ? <Alerta tipo="erro">{erro}</Alerta> : <Carregando />
}

const min = (m: number | null | undefined) => (m == null ? '—' : `${m.toLocaleString('pt-PT', { maximumFractionDigits: 1 })} min`)
const pct = (p: number | null | undefined) => (p == null ? '—' : `${p.toLocaleString('pt-PT', { maximumFractionDigits: 1 })} %`)
const hora = (h: number) => `${String(h).padStart(2, '0')}h-${String(h + 1).padStart(2, '0')}h`

// ---------------- Contas a pagar ----------------

export function ContasPagarVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useRelatorio<RelatorioContasPagar>('contas-pagar', { desde, ate }, [desde, ate])
  const r = rel.dados
  if (!r) return <Estado erro={rel.erro} />
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        [`Em dívida hoje (${dataCurta(r.hoje)})`, formatarMoeda(r.totalEmDivida), `${r.comprasEmAberto.length} compras por pagar`],
        ['Vencido', formatarMoeda(r.totalVencido), 'Passou a data de vencimento'],
        ['Pago no período', formatarMoeda(r.pagoNoPeriodo), `${r.pagamentos.filter((p) => !p.anulado).length} pagamentos`],
        ['Fornecedores em dívida', String(r.dividas.length), 'Com compras por pagar'],
      ]} />
      <Seccao titulo="Dívida por fornecedor (hoje)" exportar={() => tabela('contas-pagar-fornecedores', ['Fornecedor', 'Compras', 'Não vencido', '1-30 dias', '31-60 dias', '61-90 dias', '+90 dias', 'Em dívida'],
        r.dividas.map((d) => [d.fornecedor, d.compras, d.naoVencido, d.ate30, d.de31a60, d.de61a90, d.mais90, d.emDivida]))}>
        <Tabela colunas={['Fornecedor', 'Compras', 'Não vencido', '1-30 d', '31-60 d', '61-90 d', '+90 d', 'Em dívida']} direita={[1, 2, 3, 4, 5, 6, 7]}
          alerta={(i) => r.dividas[i].emDivida > r.dividas[i].naoVencido}
          linhas={r.dividas.map((d) => [d.fornecedor, d.compras, formatarMoeda(d.naoVencido), formatarMoeda(d.ate30), formatarMoeda(d.de31a60), formatarMoeda(d.de61a90),
            formatarMoeda(d.mais90), <strong key="t">{formatarMoeda(d.emDivida)}</strong>])} />
      </Seccao>
      <Seccao titulo="Compras por pagar" exportar={() => tabela('compras-por-pagar', ['Fornecedor', 'Documento', 'Data', 'Vencimento', 'Dias de atraso', 'Total', 'Pago', 'Em dívida'],
        r.comprasEmAberto.map((c) => [c.fornecedor, c.documento, dataCurta(c.data), c.vencimento ? dataCurta(c.vencimento) : null, c.diasAtraso, c.total, c.pago, c.emDivida]))}>
        <Tabela colunas={['Fornecedor', 'Documento', 'Vencimento', 'Total', 'Pago', 'Em dívida']} direita={[3, 4, 5]} alerta={(i) => r.comprasEmAberto[i].diasAtraso > 0}
          linhas={r.comprasEmAberto.map((c) => [c.fornecedor, c.documento, c.vencimento ? `${dataCurta(c.vencimento)}${c.diasAtraso > 0 ? ` (+${c.diasAtraso} d)` : ''}` : '—',
            formatarMoeda(c.total), formatarMoeda(c.pago), formatarMoeda(c.emDivida)])} />
      </Seccao>
      <Seccao titulo="Pagamentos no período" exportar={() => tabela(`pagamentos-fornecedores_${desde}_${ate}`, ['Data/hora', 'Fornecedor', 'Documento', 'Método', 'Referência', 'Valor', 'Anulado'],
        r.pagamentos.map((p) => [formatarDataHora(p.dataUtc), p.fornecedor, p.documento, p.metodo, p.referencia, p.valor, p.anulado ? 'Sim' : 'Não']))}>
        <Tabela colunas={['Data', 'Fornecedor', 'Documento', 'Método', 'Valor']} direita={[4]} alerta={(i) => r.pagamentos[i].anulado}
          linhas={r.pagamentos.map((p) => [formatarDataHora(p.dataUtc), p.fornecedor, p.documento, p.metodo + (p.anulado ? ' (anulado)' : ''), formatarMoeda(p.valor)])} />
      </Seccao>
      <Metodologia>
        Compras confirmadas menos os pagamentos ao fornecedor registados na compra (Compras → abrir a compra → Pagamentos). Vencido: passou a data de
        vencimento; compras sem vencimento contam como não vencidas. Os pagamentos são pela data do pagamento.
      </Metodologia>
    </div>
  )
}

// ---------------- Vendas por hora ----------------

export function HorasVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useRelatorio<RelatorioHoras>('horas', { desde, ate }, [desde, ate])
  const r = rel.dados
  if (!r) return <Estado erro={rel.erro} />
  const maximo = Math.max(1, ...r.celulas.map((c) => c.valor))
  const horas = [...new Set(r.celulas.map((c) => c.hora))].sort((a, b) => a - b)
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Vendas', formatarMoeda(r.valor), `${r.pedidos} pedidos pagos`],
        ['Hora de mais vendas', r.horaPico == null ? '—' : hora(r.horaPico), 'Pela hora do pagamento'],
        ['Melhor dia da semana', r.diaPico ?? '—', 'Maior média por dia'],
        ['Valor médio por pedido', formatarMoeda(r.pedidos ? r.valor / r.pedidos : 0), ''],
      ]} />
      <Seccao titulo="Mapa: dia da semana × hora" exportar={() => tabela(`vendas-hora-dia_${desde}_${ate}`, ['Dia', 'Hora', 'Pedidos', 'Valor (MT)'],
        r.celulas.map((c) => [r.porDiaSemana[c.diaSemana].nome, hora(c.hora), c.pedidos, c.valor]))}>
        {horas.length === 0 ? <p className="text-sm text-slate-500">Sem vendas no período.</p> : (
          <div className="overflow-x-auto">
            <table className="text-xs tabular-nums">
              <thead><tr><th scope="col" className="pr-2" />{horas.map((h) => <th key={h} scope="col" className="px-1 font-medium text-slate-600">{String(h).padStart(2, '0')}h</th>)}</tr></thead>
              <tbody>
                {r.porDiaSemana.map((d) => (
                  <tr key={d.diaSemana}>
                    <th scope="row" className="pr-2 text-left font-medium text-slate-600">{d.nome.slice(0, 3)}</th>
                    {horas.map((h) => {
                      const c = r.celulas.find((x) => x.diaSemana === d.diaSemana && x.hora === h)
                      const a = c ? 0.12 + 0.88 * (c.valor / maximo) : 0
                      return (
                        <td key={h} className="p-0.5">
                          <div className="flex h-8 w-10 items-center justify-center rounded" style={{ background: c ? `rgba(37,99,235,${a})` : 'transparent', color: a > 0.55 ? '#fff' : undefined }}
                            title={c ? `${d.nome} ${hora(h)}: ${c.pedidos} pedidos · ${formatarMoeda(c.valor)}` : undefined}>{c?.pedidos ?? ''}</div>
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="mt-1 text-xs text-slate-500">Número de pedidos em cada hora; quanto mais escuro, mais valor vendido.</p>
          </div>
        )}
      </Seccao>
      <div className="grid gap-4 lg:grid-cols-2">
        <Seccao titulo="Por hora" exportar={() => tabela(`vendas-por-hora_${desde}_${ate}`, ['Hora', 'Pedidos', 'Valor médio', 'Valor'], r.porHora.map((h) => [hora(h.hora), h.pedidos, h.valorMedio, h.valor]))}>
          <Tabela colunas={['Hora', 'Pedidos', 'Valor médio', 'Valor']} direita={[1, 2, 3]}
            linhas={r.porHora.map((h) => [hora(h.hora), h.pedidos, formatarMoeda(h.valorMedio), formatarMoeda(h.valor)])} />
        </Seccao>
        <Seccao titulo="Por dia da semana" exportar={() => tabela(`vendas-dia-semana_${desde}_${ate}`, ['Dia', 'Pedidos', 'Dias no período', 'Média por dia', 'Valor'],
          r.porDiaSemana.map((d) => [d.nome, d.pedidos, d.dias, d.mediaPorDia, d.valor]))}>
          <Tabela colunas={['Dia', 'Pedidos', 'Média por dia', 'Valor']} direita={[1, 2, 3]}
            linhas={r.porDiaSemana.map((d) => [d.nome, d.pedidos, formatarMoeda(d.mediaPorDia), formatarMoeda(d.valor)])} />
        </Seccao>
      </div>
      <Metodologia>Pedidos pagos, pela hora local do pagamento. Média por dia: valor do dia da semana a dividir pelas vezes que esse dia aparece no período.</Metodologia>
    </div>
  )
}

// ---------------- Cotações ----------------

const NOME_COTACAO: Record<SituacaoCotacao, string> = { Aberta: 'Em aberto', Expirada: 'Expirada', Convertida: 'Convertida', Anulada: 'Anulada' }

export function CotacoesVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useRelatorio<RelatorioCotacoes>('cotacoes', { desde, ate }, [desde, ate])
  const r = rel.dados
  if (!r) return <Estado erro={rel.erro} />
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Emitidas', formatarMoeda(r.valorEmitido), `${r.emitidas} cotações`],
        ['Convertidas em factura', formatarMoeda(r.valorConvertido), `${r.convertidas} · conversão ${pct(r.taxaConversao)}`],
        ['Em aberto', formatarMoeda(r.valorAberto), `${r.abertas} ainda válidas`],
        ['Expiradas', formatarMoeda(r.valorExpirado), `${r.expiradas} sem resposta · ${r.anuladas} anuladas`],
      ]} />
      {r.diasMedioConversao != null && <p className="text-sm text-slate-600">Em média, uma cotação passa a factura em {r.diasMedioConversao.toLocaleString('pt-PT')} dias.</p>}
      <Seccao titulo="Cotações do período" exportar={() => tabela(`cotacoes_${desde}_${ate}`, ['Data', 'Número', 'Cliente', 'Válida até', 'Situação', 'Factura', 'Dias até converter', 'Total'],
        r.linhas.map((c) => [formatarDataHora(c.emitidoEmUtc), c.numero, c.cliente, c.validaAte ? dataCurta(c.validaAte) : null, NOME_COTACAO[c.situacao], c.factura, c.diasAteConversao, c.total]))}>
        {r.cortado && <Alerta tipo="aviso">Mostram-se só as primeiras 5000 cotações.</Alerta>}
        <Tabela colunas={['Data', 'Número', 'Cliente', 'Válida até', 'Situação', 'Total']} direita={[5]} alerta={(i) => r.linhas[i].situacao === 'Expirada'}
          linhas={r.linhas.map((c) => [formatarDataHora(c.emitidoEmUtc), c.numero, c.cliente, c.validaAte ? dataCurta(c.validaAte) : '—',
            NOME_COTACAO[c.situacao] + (c.factura ? ` → ${c.factura}` : ''), formatarMoeda(c.total)])} />
      </Seccao>
      <Metodologia>Cotações pela data de emissão. Taxa de conversão: convertidas sobre as não anuladas. Expirada: em aberto e já passou a data de validade.</Metodologia>
    </div>
  )
}

// ---------------- Cozinha ----------------

export function CozinhaVista({ desde, ate }: { desde: string; ate: string }) {
  const [limite, setLimite] = useState(20)
  const rel = useRelatorio<RelatorioCozinha>('cozinha', { desde, ate, limiteMinutos: limite }, [desde, ate, limite])
  const r = rel.dados
  const escolha = (
    <label className="flex items-center gap-2 text-sm print:hidden">Atrasado a partir de
      <select className="campo w-auto" value={limite} onChange={(e) => setLimite(Number(e.target.value))}>
        {[10, 15, 20, 25, 30, 45].map((m) => <option key={m} value={m}>{m} min</option>)}
      </select>
    </label>
  )
  if (!r) return <div className="space-y-3">{escolha}<Estado erro={rel.erro} /></div>
  const linhaTempo = (l: RelatorioCozinha['porPrato'][number]) => [l.nome, l.itens, min(l.mediaMinutos), min(l.maximoMinutos), l.atrasados]
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      {escolha}
      <Indicadores itens={[
        ['Tempo médio', min(r.mediaMinutos), `${r.itens} itens preparados`],
        ['Mediana', min(r.medianaMinutos), 'Metade dos itens fica abaixo'],
        [`Acima de ${r.limiteMinutos} min`, String(r.atrasados), pct(r.itens ? (100 * r.atrasados) / r.itens : null)],
        ['Sem «pronto»', String(r.porTerminar), 'Enviados e não marcados prontos'],
      ]} />
      <div className="grid gap-4 lg:grid-cols-2">
        <Seccao titulo="Por estação" exportar={() => tabela(`cozinha-estacoes_${desde}_${ate}`, ['Estação', 'Itens', 'Média (min)', 'Máximo (min)', 'Atrasados'],
          r.porEstacao.map((l) => [l.nome, l.itens, l.mediaMinutos, l.maximoMinutos, l.atrasados]))}>
          <Tabela colunas={['Estação', 'Itens', 'Média', 'Máximo', 'Atrasados']} direita={[1, 2, 3, 4]} linhas={r.porEstacao.map(linhaTempo)} />
        </Seccao>
        <Seccao titulo="Itens mais lentos" exportar={() => tabela(`cozinha-lentos_${desde}_${ate}`, ['Enviado', 'Pedido', 'Mesa', 'Prato', 'Estação', 'Minutos'],
          r.maisLentos.map((x) => [formatarDataHora(x.enviadoEmUtc), x.pedido, x.mesa, x.produto, x.estacao, x.minutos]))}>
          <Tabela colunas={['Enviado', 'Pedido', 'Prato', 'Tempo']} direita={[3]}
            linhas={r.maisLentos.map((x) => [formatarDataHora(x.enviadoEmUtc), x.pedido + (x.mesa ? ` (${x.mesa})` : ''), x.produto, min(x.minutos)])} />
        </Seccao>
      </div>
      <Seccao titulo="Por prato (mais lentos primeiro)" exportar={() => tabela(`cozinha-pratos_${desde}_${ate}`, ['Prato', 'Estação', 'Itens', 'Média (min)', 'Máximo (min)', 'Atrasados'],
        r.porPrato.map((l) => [l.nome, l.estacao, l.itens, l.mediaMinutos, l.maximoMinutos, l.atrasados]))}>
        <Tabela colunas={['Prato', 'Itens', 'Média', 'Máximo', 'Atrasados']} direita={[1, 2, 3, 4]} alerta={(i) => r.porPrato[i].atrasados > 0} linhas={r.porPrato.map(linhaTempo)} />
      </Seccao>
      <Metodologia>Do envio para a cozinha até ser marcado «pronto» no ecrã da cozinha, pela data do envio. Itens cancelados não contam.</Metodologia>
    </div>
  )
}

// ---------------- Delivery ----------------

export function DeliveryVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useRelatorio<RelatorioDelivery>('delivery', { desde, ate }, [desde, ate])
  const r = rel.dados
  if (!r) return <Estado erro={rel.erro} />
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Vendido', formatarMoeda(r.valor), `${r.pedidos} pedidos`],
        ['Taxas de entrega', formatarMoeda(r.taxaEntrega), 'Incluídas no vendido'],
        ['Tempo médio até entregar', min(r.mediaMinutosEntrega), 'Delivery: do pedido à entrega'],
        ['Cancelados', String(r.cancelados), ''],
      ]} />
      <div className="grid gap-4 lg:grid-cols-2">
        <Seccao titulo="Por tipo" exportar={() => tabela(`delivery-tipos_${desde}_${ate}`, ['Tipo', 'Pedidos', 'Pagos', 'Cancelados', 'Tempo médio (min)', 'Valor'],
          r.porTipo.map((t) => [t.tipo === 'Delivery' ? 'Delivery' : 'Take Away', t.pedidos, t.pagos, t.cancelados, t.mediaMinutos, t.valor]))}>
          <Tabela colunas={['Tipo', 'Pedidos', 'Cancelados', 'Valor']} direita={[1, 2, 3]}
            linhas={r.porTipo.map((t) => [t.tipo === 'Delivery' ? 'Delivery' : 'Take Away', t.pedidos, t.cancelados, formatarMoeda(t.valor)])} />
        </Seccao>
        <Seccao titulo="Por estafeta" exportar={() => tabela(`delivery-estafetas_${desde}_${ate}`, ['Estafeta', 'Entregas', 'Tempo médio (min)', 'Taxas', 'Valor'],
          r.porEstafeta.map((e) => [e.estafeta, e.entregas, e.mediaMinutos, e.taxaEntrega, e.valor]))}>
          <Tabela colunas={['Estafeta', 'Entregas', 'Tempo médio', 'Valor']} direita={[1, 2, 3]}
            linhas={r.porEstafeta.map((e) => [e.estafeta, e.entregas, min(e.mediaMinutos), formatarMoeda(e.valor)])} />
        </Seccao>
      </div>
      <Metodologia>Pedidos de Delivery e Take Away pela data em que foram criados; valor só dos pagos. O tempo de entrega conta até o pedido ser marcado «Entregue».</Metodologia>
    </div>
  )
}

// ---------------- Mesas ----------------

export function MesasVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useRelatorio<RelatorioMesas>('mesas', { desde, ate }, [desde, ate])
  const r = rel.dados
  if (!r) return <Estado erro={rel.erro} />
  const linha = (l: RelatorioMesas['porMesa'][number], nome: string): Celula[] => [nome, l.pedidos, l.pessoas, min(l.mediaMinutos), l.valorPorPessoa == null ? '—' : formatarMoeda(l.valorPorPessoa), formatarMoeda(l.valor)]
  const exportar = (nome: string, linhas: RelatorioMesas['porMesa'], comZona: boolean) => tabela(`${nome}_${desde}_${ate}`,
    [...(comZona ? ['Mesa', 'Zona'] : ['Zona']), 'Contas', 'Pessoas', 'Tempo médio (min)', 'Valor por pessoa', 'Valor'],
    linhas.map((l) => [...(comZona ? [l.mesa, l.zona] : [l.zona]), l.pedidos, l.pessoas, l.mediaMinutos, l.valorPorPessoa, l.valor]))
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Vendido nas mesas', formatarMoeda(r.valor), `${r.pedidos} contas pagas`],
        ['Pessoas', String(r.pessoas), 'Indicadas ao abrir a mesa'],
        ['Tempo médio por mesa', min(r.mediaMinutos), 'Da abertura ao pagamento'],
        ['Valor por pessoa', r.valorPorPessoa == null ? '—' : formatarMoeda(r.valorPorPessoa), ''],
      ]} />
      <Seccao titulo="Por zona" exportar={() => exportar('mesas-zonas', r.porZona, false)}>
        <Tabela colunas={['Zona', 'Contas', 'Pessoas', 'Tempo médio', 'Por pessoa', 'Valor']} direita={[1, 2, 3, 4, 5]} linhas={r.porZona.map((l) => linha(l, l.zona))} />
      </Seccao>
      <Seccao titulo="Por mesa" exportar={() => exportar('mesas', r.porMesa, true)}>
        <Tabela colunas={['Mesa', 'Contas', 'Pessoas', 'Tempo médio', 'Por pessoa', 'Valor']} direita={[1, 2, 3, 4, 5]} linhas={r.porMesa.map((l) => linha(l, `${l.mesa} (${l.zona})`))} />
      </Seccao>
      <Metodologia>Contas de mesa pagas no período, pela data do pagamento. Tempo: da abertura do pedido ao pagamento.</Metodologia>
    </div>
  )
}

// ---------------- Pontos ----------------

export function PontosVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useRelatorio<RelatorioPontos>('pontos', { desde, ate }, [desde, ate])
  const r = rel.dados
  if (!r) return <Estado erro={rel.erro} />
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Pontos ganhos', r.ganhos.toLocaleString('pt-PT'), 'Nos pedidos pagos'],
        ['Pontos usados', r.usados.toLocaleString('pt-PT'), 'Trocados pelos clientes'],
        ['Ajustes e estornos', (r.ajustes + r.estornos).toLocaleString('pt-PT'), `ajustes ${r.ajustes} · estornos ${r.estornos}`],
        ['Saldo de hoje', r.saldoTotal.toLocaleString('pt-PT'), `${r.clientesComSaldo} clientes com pontos`],
      ]} />
      <Seccao titulo="Por cliente" exportar={() => tabela(`pontos_${desde}_${ate}`, ['Cliente', 'Telefone', 'Ganhos', 'Usados', 'Ajustes', 'Estornos', 'Saldo hoje'],
        r.clientes.map((c) => [c.cliente, c.telefone, c.ganhos, c.usados, c.ajustes, c.estornos, c.saldo]))}>
        <Tabela colunas={['Cliente', 'Ganhos', 'Usados', 'Ajustes', 'Saldo hoje']} direita={[1, 2, 3, 4]}
          linhas={r.clientes.map((c) => [c.cliente, c.ganhos, c.usados, c.ajustes + c.estornos, <strong key="s">{c.saldo}</strong>])} />
      </Seccao>
      <Metodologia>Movimentos de pontos pela data; o saldo é o de hoje. Os pontos só são ganhos se a fidelização estiver activa nas Configurações.</Metodologia>
    </div>
  )
}

// ---------------- Reservas ----------------

const NOME_RESERVA: Record<SituacaoReserva, string> = { Compareceu: 'Compareceu', Faltou: 'Faltou', Cancelada: 'Cancelada', PorVir: 'Por vir' }

export function ReservasVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useRelatorio<RelatorioReservas>('reservas', { desde, ate }, [desde, ate])
  const r = rel.dados
  if (!r) return <Estado erro={rel.erro} />
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Reservas', String(r.reservas), `${r.pessoas} pessoas (sem as canceladas)`],
        ['Compareceram', String(r.compareceram), `taxa de comparência ${pct(r.taxaComparencia)}`],
        ['Faltaram', String(r.faltaram), 'Não chegaram nem foram canceladas'],
        ['Canceladas / por vir', `${r.canceladas} / ${r.porVir}`, ''],
      ]} />
      <Seccao titulo="Por dia da semana" exportar={() => tabela(`reservas-dia-semana_${desde}_${ate}`, ['Dia', 'Reservas', 'Pessoas'], r.porDiaSemana.map((d) => [d.nome, d.quantidade, d.valor]))}>
        <Tabela colunas={['Dia', 'Reservas', 'Pessoas']} direita={[1, 2]} linhas={r.porDiaSemana.map((d) => [d.nome, d.quantidade, d.valor])} />
      </Seccao>
      <Seccao titulo="Reservas do período" exportar={() => tabela(`reservas_${desde}_${ate}`, ['Data/hora', 'Cliente', 'Telefone', 'Pessoas', 'Mesa', 'Situação', 'Motivo do cancelamento'],
        r.linhas.map((x) => [formatarDataHora(x.inicioUtc), x.cliente, x.telefone, x.pessoas, x.mesa, NOME_RESERVA[x.situacao], x.motivo]))}>
        {r.cortado && <Alerta tipo="aviso">Mostram-se só as primeiras 5000 reservas.</Alerta>}
        <Tabela colunas={['Data/hora', 'Cliente', 'Mesa', 'Situação', 'Pessoas']} direita={[4]} alerta={(i) => r.linhas[i].situacao === 'Faltou'}
          linhas={r.linhas.map((x) => [formatarDataHora(x.inicioUtc), x.cliente, x.mesa ?? '—', NOME_RESERVA[x.situacao], x.pessoas])} />
      </Seccao>
      <Metodologia>Reservas pela data marcada. Faltou: pendente ou confirmada e já passou mais de 1 hora da hora marcada sem o cliente chegar.</Metodologia>
    </div>
  )
}
