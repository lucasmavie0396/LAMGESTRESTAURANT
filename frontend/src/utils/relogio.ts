// Relógio do servidor: o sistema regista tudo com a data e a hora do servidor. Se estiverem erradas (ex.: adiantadas por engano),
// os dias e as vendas ficam com a data errada e, depois de acertar, a licença e os dias seguintes podem bloquear. Estes avisos
// aparecem antes de abrir o dia e em Configurações → Sistema.
import { dataCurta } from './datas'

/** Diferença a partir da qual se avisa (minutos) entre a hora do servidor e a deste computador. */
export const LIMITE_DIFERENCA_MIN = 10

/** Minutos (com sinal) que o servidor está à frente deste computador. */
export function diferencaServidorMin(servidorUtc: string, agora = Date.now()) {
  return Math.round((Date.parse(servidorUtc) - agora) / 60_000)
}

/** Data local deste computador (AAAA-MM-DD, fuso do próprio computador). */
export function dataDoComputador(agora = new Date()) {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${agora.getFullYear()}-${p(agora.getMonth() + 1)}-${p(agora.getDate())}`
}

function diasEntre(de: string, ate: string) {
  return Math.round((Date.parse(`${ate}T00:00:00Z`) - Date.parse(`${de}T00:00:00Z`)) / 86_400_000)
}

/** Texto da diferença («3 h 20 min», «2 dias»…). */
export function descreverDiferenca(minutos: number) {
  const m = Math.abs(minutos)
  if (m >= 2 * 1440) return `${Math.round(m / 1440)} dias`
  if (m >= 60) return `${Math.floor(m / 60)} h ${m % 60} min`
  return `${m} min`
}

/** Aviso de relógio (null se a hora do servidor e a deste computador baterem certo). */
export function avisoRelogio(servidorUtc: string, agora = Date.now()) {
  const dif = diferencaServidorMin(servidorUtc, agora)
  if (Math.abs(dif) <= LIMITE_DIFERENCA_MIN) return null
  return `A hora do servidor está ${descreverDiferenca(dif)} ${dif > 0 ? 'à frente' : 'atrás'} da deste computador. ` +
    'Confirme qual está certa e acerte a data e a hora do Windows do servidor antes de continuar.'
}

/**
 * Avisos antes de abrir o dia: a data a abrir não é a deste computador, a hora do servidor difere da deste computador, ou passaram
 * vários dias desde o último dia (sinal de um relógio adiantado). Com avisos, o ecrã pede para confirmar a data.
 */
export function avisosAbrirDia(o: { data: string; agoraServidorUtc: string; ultimoDia: string | null; agora?: Date }) {
  const agora = o.agora ?? new Date()
  const avisos: string[] = []
  const relogio = avisoRelogio(o.agoraServidorUtc, agora.getTime())
  if (relogio) avisos.push(relogio)
  const local = dataDoComputador(agora)
  if (o.data !== local) avisos.push(`A data a abrir (${dataCurta(o.data)}) não é a data deste computador (${dataCurta(local)}).`)
  if (o.ultimoDia) {
    const dias = diasEntre(o.ultimoDia, o.data)
    if (dias > 1) avisos.push(`O último dia de operação foi ${dataCurta(o.ultimoDia)}: vai abrir ${dias} dias depois.`)
  }
  return avisos
}
