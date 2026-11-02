// Formatação para apresentação. Os valores vêm calculados do servidor; aqui só se formatam.

export interface OpcoesRegionais {
  moeda: string
  simboloMoeda: string
  fusoHorario: string
}

export const REGIONAL_PADRAO: OpcoesRegionais = {
  moeda: 'MZN',
  simboloMoeda: 'MT',
  fusoHorario: 'Africa/Maputo',
}

const formatadorNumero = new Intl.NumberFormat('pt-MZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** Ex.: 1234.5 → "1 234,50 MT" (o símbolo é configurável). */
export function formatarMoeda(valor: number, opcoes: OpcoesRegionais = REGIONAL_PADRAO): string {
  return `${formatadorNumero.format(valor)} ${opcoes.simboloMoeda}`
}

/** Converte uma data UTC (ISO do servidor) para data/hora local no fuso configurado. */
export function formatarDataHora(isoUtc: string, opcoes: OpcoesRegionais = REGIONAL_PADRAO): string {
  return new Intl.DateTimeFormat('pt-MZ', {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: opcoes.fusoHorario,
  }).format(new Date(isoUtc))
}
