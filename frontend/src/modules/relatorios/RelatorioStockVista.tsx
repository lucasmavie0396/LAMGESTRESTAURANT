import { api } from '../../api/cliente'
import { BarrasHorizontais } from '../../components/Graficos'
import { Alerta, Carregando, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { ArtigoValor, RelatorioStock } from '../../types/api'
import { descarregarCsv } from '../../utils/csv'
import { formatarMoeda } from '../../utils/formatar'
import { NOME_MOVIMENTO } from '../stock/comum'
import { Indicadores, Metodologia, Seccao } from './comum'
import { corSinal, n3 } from './formato'

function TabelaArtigos({ linhas, vazio }: { linhas: ArtigoValor[]; vazio: string }) {
  if (linhas.length === 0) return <Vazio texto={vazio} />
  return (
    <div className="max-h-80 overflow-auto print:max-h-none">
      <table className="w-full text-left text-sm">
        <thead className="sticky top-0 bg-white text-slate-600">
          <tr className="border-b border-slate-200">
            <th scope="col" className="py-2">Artigo</th>
            <th scope="col" className="py-2 text-right">Quantidade</th>
            <th scope="col" className="py-2 text-right">Valor</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 tabular-nums">
          {linhas.map((l) => (
            <tr key={l.artigoId}>
              <td className="py-1.5"><span className="font-medium">{l.artigo}</span> <span className="text-xs text-slate-500">{l.codigo}</span></td>
              <td className={`py-1.5 text-right ${corSinal(l.quantidade)}`}>{n3.format(l.quantidade)} {l.unidade}</td>
              <td className={`py-1.5 text-right ${corSinal(l.valor)}`}>{formatarMoeda(l.valor)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

const csvArtigos = (nome: string, linhas: ArtigoValor[]) =>
  descarregarCsv(nome, ['Código', 'Artigo', 'Unidade', 'Quantidade', 'Valor (MT)'], linhas.map((l) => [l.codigo, l.artigo, l.unidade, l.quantidade, l.valor]))

export function RelatorioStockVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useDados(() => api.get<RelatorioStock>(`/api/relatorios/stock?desde=${desde}&ate=${ate}`), [desde, ate])
  const sufixo = `${desde}_${ate}`

  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const soma = (l: ArtigoValor[]) => l.reduce((s, x) => s + x.valor, 0)
  const totalCompras = r.comprasPorFornecedor.reduce((s, l) => s + l.valor, 0)
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Valor do stock (actual)', formatarMoeda(r.valorStockActual), `${r.artigosActivos} artigos activos · ${r.artigosAbaixoMinimo} abaixo do mínimo`],
        ['Consumo no período', formatarMoeda(soma(r.consumo)), 'Saídas por venda (fichas técnicas)'],
        ['Ajustes e perdas', formatarMoeda(soma(r.ajustesPerdas)), 'Negativo = perda ou falta no inventário'],
        ['Compras confirmadas', formatarMoeda(totalCompras), `${r.comprasPorFornecedor.reduce((s, l) => s + l.quantidade, 0)} documentos, com IVA`],
      ]} />

      <div className="grid gap-4 lg:grid-cols-3">
        <Seccao titulo="Valor actual por tipo de artigo">
          <BarrasHorizontais linhas={r.valorPorTipo.map((l) => ({ rotulo: l.rotulo, valor: l.valor, extra: `(${l.quantidade})` }))} vazio="Sem stock." />
        </Seccao>
        <Seccao titulo="Valor actual por armazém">
          <BarrasHorizontais linhas={r.valorPorArmazem.map((l) => ({ rotulo: l.rotulo, valor: l.valor }))} vazio="Sem stock." />
        </Seccao>
        <Seccao titulo="Compras por fornecedor"
          csv={() => descarregarCsv(`compras-fornecedor_${sufixo}`, ['Fornecedor', 'Compras', 'Total com IVA (MT)'], r.comprasPorFornecedor.map((l) => [l.rotulo, l.quantidade, l.valor]))}>
          <BarrasHorizontais linhas={r.comprasPorFornecedor.map((l) => ({ rotulo: l.rotulo, valor: l.valor, extra: `(${l.quantidade})` }))} vazio="Sem compras confirmadas no período." />
        </Seccao>
      </div>

      <Seccao titulo="Movimentos por tipo"
        csv={() => descarregarCsv(`movimentos-tipo_${sufixo}`, ['Tipo', 'Movimentos', 'Valor (MT)'], r.movimentosPorTipo.map((t) => [NOME_MOVIMENTO[t.tipo], t.movimentos, t.valor]))}>
        {r.movimentosPorTipo.length === 0 ? <Vazio texto="Sem movimentos no período." /> : (
          <table className="w-full max-w-2xl text-left text-sm">
            <thead className="text-slate-600">
              <tr className="border-b border-slate-200">
                <th scope="col" className="py-2">Tipo</th>
                <th scope="col" className="py-2 text-right">Movimentos</th>
                <th scope="col" className="py-2 text-right">Valor</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {r.movimentosPorTipo.map((t) => (
                <tr key={t.tipo}>
                  <td className="py-1.5">{NOME_MOVIMENTO[t.tipo]}</td>
                  <td className="py-1.5 text-right">{t.movimentos}</td>
                  <td className={`py-1.5 text-right ${corSinal(t.valor)}`}>{formatarMoeda(t.valor)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Seccao>

      <div className="grid gap-4 lg:grid-cols-3">
        <Seccao titulo="Consumo por artigo" csv={() => csvArtigos(`consumo_${sufixo}`, r.consumo)}>
          <TabelaArtigos linhas={r.consumo} vazio="Sem consumo registado (produtos sem ficha técnica não baixam stock)." />
        </Seccao>
        <Seccao titulo="Ajustes, perdas e inventário" csv={() => csvArtigos(`ajustes-perdas_${sufixo}`, r.ajustesPerdas)}>
          <TabelaArtigos linhas={r.ajustesPerdas} vazio="Sem ajustes nem perdas no período." />
        </Seccao>
        <Seccao titulo="Entradas por compra" csv={() => csvArtigos(`entradas_${sufixo}`, r.entradas)}>
          <TabelaArtigos linhas={r.entradas} vazio="Sem entradas por compra no período." />
        </Seccao>
      </div>

      <Metodologia><strong>Metodologia:</strong> {r.metodologia}</Metodologia>
    </div>
  )
}
