import { api, query } from '../api/cliente'
import type { DocumentoImpressao, LinhaImpressao, Pagina } from '../types/api'
import type { Celula } from './xlsx'

/** Máximo de linhas numa exportação (as listas grandes pedem-se página a página, 200 de cada vez). */
export const MAXIMO_LINHAS_EXPORTACAO = 20_000

/** Coluna de uma lista exportada: título e como se obtém o valor de cada linha (números ficam números no Excel). */
export interface ColunaExportacao<T> { titulo: string; valor: (linha: T) => Celula }

/** Tabela pronta a exportar: nome do ficheiro, título, filtros aplicados (texto) e células. */
export interface ListaExportada { nome: string; titulo: string; filtros: string[]; cabecalho: string[]; linhas: Celula[][]; cortada: boolean }

type Parametros = Record<string, string | number | boolean | undefined | null>

/** Todas as linhas de uma lista paginada com os filtros indicados, até ao máximo. */
export async function obterTodasPaginas<T>(url: string, filtros: Parametros, maximo = MAXIMO_LINHAS_EXPORTACAO): Promise<{ itens: T[]; cortada: boolean }> {
  const itens: T[] = []
  for (let pagina = 1; ; pagina++) {
    const r = await api.get<Pagina<T>>(url + query({ ...filtros, pagina, tamanho: 200 }))
    itens.push(...r.itens)
    if (itens.length >= r.total || r.itens.length === 0) return { itens: itens.slice(0, maximo), cortada: false }
    if (itens.length >= maximo) return { itens: itens.slice(0, maximo), cortada: true }
  }
}

/** Monta a tabela a partir das linhas e das colunas. */
export function montarLista<T>(nome: string, titulo: string, filtros: (string | false | null | undefined)[], colunas: ColunaExportacao<T>[], itens: T[], cortada = false): ListaExportada {
  return {
    nome, titulo, cortada,
    filtros: filtros.filter((f): f is string => !!f),
    cabecalho: colunas.map((c) => c.titulo),
    linhas: itens.map((i) => colunas.map((c) => c.valor(i))),
  }
}

const numero = new Intl.NumberFormat('pt-MZ', { minimumFractionDigits: 0, maximumFractionDigits: 3 })
const decimal = new Intl.NumberFormat('pt-MZ', { minimumFractionDigits: 2, maximumFractionDigits: 3 })

/** Texto de uma célula no PDF: inteiros sem casas, decimais com 2 (ou 3 nas quantidades). */
export function textoCelula(c: Celula): string {
  if (c === null || c === undefined || c === '') return '—'
  if (typeof c === 'number') return Number.isInteger(c) ? numero.format(c) : decimal.format(c)
  return c
}

const linha = (texto: string, extra: Partial<LinhaImpressao> = {}): LinhaImpressao =>
  ({ texto, direita: null, alinhamento: 'Esquerda', negrito: false, grande: false, separador: false, ...extra })

/**
 * Documento A4 da lista (título, filtros, grelha e rodapé), desenhado pelo mesmo motor dos relatórios. As colunas de texto
 * são as que têm algum texto; as restantes alinham à direita como valores.
 */
export function documentoLista(l: ListaExportada, estabelecimento: string, emitidoPor: string | null, agora = new Date()): DocumentoImpressao {
  // Colunas de texto: as iniciais até à primeira coluna só de números (as listas põem o texto primeiro).
  const numerica = (i: number) => l.linhas.some((x) => typeof x[i] === 'number') && l.linhas.every((x) => typeof x[i] === 'number' || x[i] == null || x[i] === '')
  const primeira = l.cabecalho.findIndex((_, i) => numerica(i))
  const texto = primeira < 0 ? l.cabecalho.length : Math.max(1, primeira)
  const linhas: LinhaImpressao[] = [
    linha(l.titulo.toUpperCase(), { direita: `${l.linhas.length} ${l.linhas.length === 1 ? 'registo' : 'registos'}`, alinhamento: 'Centro', negrito: true, titulo: true }),
  ]
  if (l.filtros.length > 0) linhas.push(linha(`Filtros: ${l.filtros.join(' · ')}`))
  linhas.push(linha(l.titulo, { faixa: true, direita: l.cortada ? `primeiros ${l.linhas.length}` : null }))
  if (l.linhas.length === 0) linhas.push(linha('Sem registos com estes filtros.'))
  else {
    linhas.push(linha(l.cabecalho[0], { celulas: l.cabecalho, cabecalho: true, colunasTexto: texto }))
    for (const x of l.linhas) {
      const celulas = x.map(textoCelula)
      linhas.push(linha(celulas[0], { celulas, colunasTexto: texto }))
    }
  }
  linhas.push(linha('', { separador: true }))
  const quando = new Intl.DateTimeFormat('pt-MZ', { dateStyle: 'short', timeStyle: 'short' }).format(agora)
  linhas.push(linha(`Emitido em ${quando}${emitidoPor ? ` por ${emitidoPor}` : ''}`, { alinhamento: 'Centro' }))
  linhas.push(linha('Documento interno', { alinhamento: 'Centro' }))
  return { titulo: l.titulo, linhas, cabecalho: [linha(estabelecimento, { negrito: true, alinhamento: 'Centro' })] }
}
