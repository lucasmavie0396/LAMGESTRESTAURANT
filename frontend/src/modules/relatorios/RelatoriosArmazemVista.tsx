import { useState } from 'react'
import { api, query } from '../../api/cliente'
import { BarrasHorizontais } from '../../components/Graficos'
import { Alerta, Carregando, Etiqueta, Vazio } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { Categoria, RelatorioAcompanhamentos, RelatorioExistencias, RelatorioTransferencias } from '../../types/api'
import { formatarDataHora, formatarMoeda } from '../../utils/formatar'
import { Indicadores, Metodologia, Seccao } from './comum'
import { n3, percentagem, tabela } from './formato'

/** Acompanhamentos escolhidos nas vendas pagas do período. */
export function RelatorioAcompanhamentosVista({ desde, ate }: { desde: string; ate: string }) {
  const rel = useDados(() => api.get<RelatorioAcompanhamentos>('/api/relatorios/acompanhamentos' + query({ desde, ate })), [desde, ate])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const sufixo = `${desde}_${ate}`
  const grupos = [...new Set(r.porOpcao.map((o) => o.grupo))]
  const custo = r.artigos.reduce((s, a) => s + a.custo, 0)
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Escolhas', n3.format(r.total), 'Pratos × acompanhamentos'],
        ['Opções escolhidas', String(r.porOpcao.length), `${grupos.length} grupo(s)`],
        ['Pratos com acompanhamento', String(new Set(r.porPrato.map((p) => p.prato)).size), 'Pratos diferentes'],
        ['Custo estimado', formatarMoeda(custo), 'Artigos gastos pelas opções'],
      ]} />
      {r.porOpcao.length === 0 ? <div className="cartao p-4"><Vazio texto="Sem acompanhamentos escolhidos no período." /></div> : (
        <div className="grid gap-4 lg:grid-cols-2">
          {grupos.map((g) => {
            const opcoes = r.porOpcao.filter((o) => o.grupo === g)
            const total = opcoes.reduce((s, o) => s + o.vezes, 0)
            return (
              <Seccao key={g} titulo={g} exportar={() => tabela(`acompanhamentos_${g}_${sufixo}`, ['Opção', 'Vezes', 'Pratos diferentes', '%'],
                opcoes.map((o) => [o.opcao, o.vezes, o.pratos, percentagem(o.vezes, total)]))}>
                <BarrasHorizontais linhas={opcoes.map((o) => ({ rotulo: o.opcao, valor: o.vezes, extra: percentagem(o.vezes, total) }))} />
              </Seccao>
            )
          })}
        </div>
      )}
      {r.porPrato.length > 0 && (
        <Seccao titulo="Por prato" exportar={() => tabela(`acompanhamentos-prato_${sufixo}`, ['Prato', 'Acompanhamento', 'Vezes'], r.porPrato.map((p) => [p.prato, p.opcao, p.vezes]))}>
          <table className="w-full max-w-3xl text-left text-sm">
            <thead className="text-slate-600"><tr className="border-b border-slate-200"><th className="py-2">Prato</th><th className="py-2">Acompanhamento</th><th className="py-2 text-right">Vezes</th></tr></thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {r.porPrato.map((p) => <tr key={`${p.prato}|${p.opcao}`}><td className="py-1.5 font-medium">{p.prato}</td><td className="py-1.5">{p.opcao}</td><td className="py-1.5 text-right">{n3.format(p.vezes)}</td></tr>)}
            </tbody>
          </table>
        </Seccao>
      )}
      {r.artigos.length > 0 && (
        <Seccao titulo="Artigos gastos (estimado)" exportar={() => tabela(`acompanhamentos-artigos_${sufixo}`, ['Artigo', 'Quantidade', 'Unidade', 'Custo (MT)'], r.artigos.map((a) => [a.artigo, a.quantidade, a.unidade, a.custo]))}>
          <table className="w-full max-w-3xl text-left text-sm">
            <thead className="text-slate-600"><tr className="border-b border-slate-200"><th className="py-2">Artigo</th><th className="py-2 text-right">Quantidade</th><th className="py-2 text-right">Custo</th></tr></thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {r.artigos.map((a) => <tr key={a.artigoId}><td className="py-1.5">{a.artigo}</td><td className="py-1.5 text-right">{n3.format(a.quantidade)} {a.unidade}</td><td className="py-1.5 text-right">{formatarMoeda(a.custo)}</td></tr>)}
            </tbody>
          </table>
        </Seccao>
      )}
      <Metodologia>{r.metodologia}</Metodologia>
    </div>
  )
}

/** Existências de cada artigo em cada armazém (o armazém vem do filtro da página). */
export function RelatorioExistenciasVista({ armazemId }: { armazemId: number | null }) {
  const [categoria, setCategoria] = useState('')
  const categorias = useDados(() => api.get<Categoria[]>('/api/categorias'), [])
  const rel = useDados(() => api.get<RelatorioExistencias>('/api/relatorios/existencias' + query({ armazemId: armazemId ?? undefined, categoriaId: categoria || undefined })),
    [armazemId, categoria])
  const r = rel.dados
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <select className="campo w-64 print:hidden" value={categoria} onChange={(e) => setCategoria(e.target.value)} aria-label="Filtrar por categoria">
        <option value="">Todas as categorias</option>
        {(categorias.dados ?? []).filter((c) => !c.categoriaPaiId).map((c) => <option key={c.id} value={c.id}>{c.nome}</option>)}
      </select>
      {rel.erro && <Alerta tipo="erro">{rel.erro}</Alerta>}
      {!r && !rel.erro && <Carregando />}
      {r && (
        <>
          <Indicadores itens={[
            ['Valor em stock', formatarMoeda(r.valorTotal), r.armazem ?? 'Todos os armazéns'],
            ['Linhas', String(r.linhas.length), 'Artigo × armazém com stock'],
            ['Abaixo do mínimo', String(r.abaixoMinimo), 'No armazém habitual do artigo'],
            ['Armazéns', String(r.porArmazem.length), 'Com existências'],
          ]} />
          {r.linhas.length === 0 ? <div className="cartao p-4"><Vazio texto="Sem existências." /></div> : r.porArmazem.map((a) => {
            const linhas = r.linhas.filter((l) => l.armazem === a.rotulo)
            return (
              <Seccao key={a.rotulo} titulo={`${a.rotulo} · ${formatarMoeda(a.valor)}`}
                exportar={() => tabela(`existencias_${a.rotulo}_${r.data}`, ['Código', 'Artigo', 'Categoria', 'Localização', 'Quantidade', 'Unidade', 'Mínimo', 'Custo médio (MT)', 'Valor (MT)'],
                  linhas.map((l) => [l.codigo, l.artigo, l.categoria ?? '', l.localizacao ?? '', l.quantidade, l.unidade, l.minimo, l.custoMedio, l.valor]))}>
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="text-slate-600">
                      <tr className="border-b border-slate-200">
                        <th className="py-2">Artigo</th><th className="py-2">Categoria</th><th className="py-2">Localização</th>
                        <th className="py-2 text-right">Quantidade</th><th className="py-2 text-right">Mínimo</th><th className="py-2 text-right">Custo médio</th><th className="py-2 text-right">Valor</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100 tabular-nums">
                      {linhas.map((l) => (
                        <tr key={`${l.armazemId}-${l.artigoId}`}>
                          <td className="py-1.5"><span className="font-medium">{l.artigo}</span> <span className="text-xs text-slate-500">{l.codigo}</span>
                            {l.abaixoMinimo && <span className="ml-2"><Etiqueta cor="vermelho">Baixo</Etiqueta></span>}</td>
                          <td className="py-1.5 text-slate-600">{l.categoria ?? '—'}</td>
                          <td className="py-1.5 text-slate-600">{l.localizacao ?? '—'}</td>
                          <td className={`py-1.5 text-right ${l.quantidade < 0 ? 'font-semibold text-red-700' : ''}`}>{n3.format(l.quantidade)} {l.unidade}</td>
                          <td className="py-1.5 text-right text-slate-600">{l.minimo > 0 ? n3.format(l.minimo) : '—'}</td>
                          <td className="py-1.5 text-right">{formatarMoeda(l.custoMedio)}</td>
                          <td className="py-1.5 text-right">{formatarMoeda(l.valor)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Seccao>
            )
          })}
          <Metodologia>Saldo actual de cada artigo activo em cada armazém × custo médio. O mínimo e a localização valem no armazém habitual do artigo.</Metodologia>
        </>
      )}
    </div>
  )
}

/** Transferências entre armazéns no período. */
export function RelatorioTransferenciasVista({ desde, ate, armazemId }: { desde: string; ate: string; armazemId: number | null }) {
  const rel = useDados(() => api.get<RelatorioTransferencias>('/api/relatorios/transferencias' + query({ desde, ate, armazemId: armazemId ?? undefined })), [desde, ate, armazemId])
  const r = rel.dados
  if (rel.erro) return <Alerta tipo="erro">{rel.erro}</Alerta>
  if (!r) return <Carregando />
  const sufixo = `${desde}_${ate}`
  return (
    <div className={`space-y-4 ${rel.aCarregar ? 'opacity-60' : ''}`}>
      <Indicadores itens={[
        ['Transferências', String(r.linhas.length), r.armazem ?? 'Todos os armazéns'],
        ['Valor transferido', formatarMoeda(r.linhas.reduce((s, l) => s + l.valor, 0)), 'Ao custo médio no momento'],
        ['Artigos', String(new Set(r.linhas.map((l) => l.artigoId)).size), 'Diferentes'],
        ['Percursos', String(r.porPercurso.length), 'Origem → destino'],
      ]} />
      {r.porPercurso.length > 0 && (
        <Seccao titulo="Por percurso">
          <BarrasHorizontais linhas={r.porPercurso.map((p) => ({ rotulo: p.rotulo, valor: p.valor, extra: `(${p.quantidade})` }))} />
        </Seccao>
      )}
      <Seccao titulo="Transferências" exportar={() => tabela(`transferencias_${sufixo}`, ['Data', 'Código', 'Artigo', 'Quantidade', 'Unidade', 'De', 'Para', 'Utilizador', 'Valor (MT)'],
        r.linhas.map((l) => [formatarDataHora(l.dataUtc), l.codigo, l.artigo, l.quantidade, l.unidade, l.origem, l.destino, l.utilizador ?? '', l.valor]))}>
        {r.linhas.length === 0 ? <Vazio texto="Sem transferências no período." /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-slate-600">
                <tr className="border-b border-slate-200">
                  <th className="py-2">Data</th><th className="py-2">Artigo</th><th className="py-2 text-right">Quantidade</th><th className="py-2">De</th><th className="py-2">Para</th>
                  <th className="py-2">Utilizador</th><th className="py-2 text-right">Valor</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {r.linhas.map((l, i) => (
                  <tr key={i}>
                    <td className="whitespace-nowrap py-1.5">{formatarDataHora(l.dataUtc)}</td>
                    <td className="py-1.5"><span className="font-medium">{l.artigo}</span> <span className="text-xs text-slate-500">{l.codigo}</span></td>
                    <td className="py-1.5 text-right">{n3.format(l.quantidade)} {l.unidade}</td>
                    <td className="py-1.5">{l.origem}</td><td className="py-1.5">{l.destino}</td>
                    <td className="py-1.5 text-slate-600">{l.utilizador ?? '—'}</td>
                    <td className="py-1.5 text-right">{formatarMoeda(l.valor)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Seccao>
    </div>
  )
}
