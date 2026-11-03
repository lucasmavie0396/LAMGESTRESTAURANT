import type { Celula } from '../../utils/xlsx'
import { hojeIso, somarDias } from '../../utils/datas'

export const n3 = new Intl.NumberFormat('pt-MZ', { maximumFractionDigits: 3 })

/** Dados de uma tabela para exportar (CSV ou Excel). */
export interface Tabela { nome: string; cabecalho: string[]; linhas: Celula[][] }

export const tabela = (nome: string, cabecalho: string[], linhas: Celula[][]): Tabela => ({ nome, cabecalho, linhas })

/** Percentagem de uma parte no total, com 1 casa (ou «—» se o total for zero). */
export const percentagem = (parte: number, total: number) =>
  total === 0 ? '—' : `${(Math.round((parte / total) * 1000) / 10).toLocaleString('pt-MZ')}%`

export type Periodo = 'hoje' | 'ontem' | 'semana' | '7dias' | 'mes' | 'mesAnterior' | 'ano' | 'personalizado'

/** Datas (locais, inclusive) de cada período. A semana começa à segunda-feira. */
export function intervalo(p: Periodo, personalizado: { desde: string; ate: string }, hoje = hojeIso()) {
  const inicioMes = `${hoje.slice(0, 7)}-01`
  switch (p) {
    case 'hoje': return { desde: hoje, ate: hoje }
    case 'ontem': return { desde: somarDias(hoje, -1), ate: somarDias(hoje, -1) }
    case 'semana': {
      const diaSemana = new Date(`${hoje}T00:00:00Z`).getUTCDay() // 0 = domingo
      return { desde: somarDias(hoje, -((diaSemana + 6) % 7)), ate: hoje }
    }
    case '7dias': return { desde: somarDias(hoje, -6), ate: hoje }
    case 'mes': return { desde: inicioMes, ate: hoje }
    case 'ano': return { desde: `${hoje.slice(0, 4)}-01-01`, ate: hoje }
    case 'mesAnterior': {
      const fim = somarDias(inicioMes, -1)
      return { desde: `${fim.slice(0, 7)}-01`, ate: fim }
    }
    default: return personalizado
  }
}

/** Cor do valor: negativo a vermelho (perdas, faltas, resultado negativo). */
export function corSinal(valor: number) {
  return valor < 0 ? 'text-red-700' : ''
}
