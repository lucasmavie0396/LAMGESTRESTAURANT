// App de venda Android (PDA): o sistema corre dentro da app, que acrescenta a ponte `window.LamgestPDA`.
// Sem a ponte (navegador normal) nada disto se aplica.

import type { DocumentoImpressao, LinhaImpressao } from '../types/api'
import { guardarTerminalLocal } from './terminalLocal'

export type DestinoImpressao = 'posto' | 'pda'

/** Métodos expostos pela app Android (android/app/.../PontePda.kt). */
export interface PontePda {
  terminalId(): number
  posto(): string
  destino(): string
  definirDestino(destino: string): void
  temImpressora(): boolean
  colunas(): number
  /** Devolve o texto do erro, ou vazio se imprimiu. */
  imprimir(linhasJson: string): string
  configurar(): void
}

declare global {
  interface Window { LamgestPDA?: PontePda }
}

export const pda = (): PontePda | null => window.LamgestPDA ?? null
export const emPda = () => pda() !== null

/** Áreas disponíveis na app: só o que é preciso para vender. */
export const ROTAS_PDA = ['/pos', '/caixa', '/alterar-palavra-passe']
export const rotaPermitidaNoPda = (caminho: string) => ROTAS_PDA.some((r) => caminho === r || caminho.startsWith(r + '/'))

/** Associa o navegador da app ao posto em que foi emparelhada (caixa, impressoras e sinal de «online»). */
export function associarPda() {
  const p = pda()
  if (p && p.terminalId() > 0) guardarTerminalLocal(p.terminalId())
}

/** O recibo e a conta saem na impressora do próprio PDA? */
export function imprimeNoPda() {
  const p = pda()
  return p !== null && p.destino() === 'pda' && p.temImpressora()
}

/** Linha pronta para a impressora: a = 0 esquerda, 1 centro, 2 direita; g = letra grande. */
export interface LinhaPda { t: string; a: 0 | 1 | 2; n: boolean; g: boolean }

/** Letra grande (32 pontos, contra os 24 da normal) leva menos caracteres por linha. */
const colunasGrande = (colunas: number) => Math.floor((colunas * 24) / 32)

function partir(texto: string, largura: number): string[] {
  const linhas: string[] = []
  let actual = ''
  for (const palavra of texto.split(/\s+/).filter(Boolean)) {
    let resto = palavra
    if (actual && actual.length + 1 + resto.length > largura) {
      linhas.push(actual)
      actual = ''
    }
    while (resto.length > largura) {
      linhas.push(resto.slice(0, largura))
      resto = resto.slice(largura)
    }
    actual = actual ? `${actual} ${resto}` : resto
  }
  if (actual) linhas.push(actual)
  return linhas.length ? linhas : ['']
}

const ALINHAMENTO = { Esquerda: 0, Centro: 1, Direita: 2 } as const

function linhaSimples(l: LinhaImpressao, colunas: number): LinhaPda[] {
  const g = l.grande
  const largura = g ? colunasGrande(colunas) : colunas
  const n = l.negrito
  if (l.direita) {
    const direita = l.direita
    if (l.texto.length + 1 + direita.length <= largura)
      return [{ t: l.texto + ' '.repeat(largura - l.texto.length - direita.length) + direita, a: 0, n, g }]
    return [...partir(l.texto, largura).map((t): LinhaPda => ({ t, a: 0, n, g })), { t: direita, a: 2, n, g }]
  }
  return partir(l.texto, largura).map((t): LinhaPda => ({ t, a: ALINHAMENTO[l.alinhamento] ?? 0, n, g }))
}

/** Tabela no talão estreito: a descrição numa linha e os valores por baixo, alinhados por coluna. */
function tabela(linhas: LinhaImpressao[], colunas: number): LinhaPda[] {
  const larguras: number[] = []
  for (const l of linhas) l.celulas!.slice(1).forEach((c, i) => { larguras[i] = Math.max(larguras[i] ?? 0, c.length) })
  const total = larguras.reduce((s, x) => s + x, 0)
  const espaco = larguras.length > 1 ? Math.min(2, Math.max(1, Math.floor((colunas - total) / (larguras.length - 1)))) : 0
  const saida: LinhaPda[] = []
  for (const l of linhas) {
    const n = l.negrito || !!l.cabecalho
    saida.push(...partir(l.celulas![0], colunas).map((t): LinhaPda => ({ t, a: 0, n, g: false })))
    const valores = l.celulas!.slice(1).map((c, i) => c.padStart(larguras[i])).join(' '.repeat(espaco))
    if (valores.length <= colunas) saida.push({ t: valores.padStart(colunas), a: 0, n, g: false }) // mantém as colunas alinhadas
    else partir(valores, colunas).forEach((t) => saida.push({ t, a: 2, n, g: false }))
  }
  return saida
}

/** Converte um documento em linhas de texto com a largura do papel do PDA (32 caracteres em 58 mm, 48 em 80 mm). */
export function linhasPda(documento: DocumentoImpressao, colunas: number): LinhaPda[] {
  const todas = [...(documento.cabecalho ?? []), ...documento.linhas]
  const saida: LinhaPda[] = []
  for (let i = 0; i < todas.length; i++) {
    const l = todas[i]
    if (l.separador) saida.push({ t: '-'.repeat(colunas), a: 0, n: false, g: false })
    else if (l.celulas && l.celulas.length > 2) {
      const bloco: LinhaImpressao[] = [l]
      while ((todas[i + 1]?.celulas?.length ?? 0) > 2) bloco.push(todas[++i])
      saida.push(...tabela(bloco, colunas))
    } else if (l.celulas?.length === 2) saida.push(...linhaSimples({ ...l, texto: l.celulas[0], direita: l.celulas[1] }, colunas))
    else saida.push(...linhaSimples(l, colunas))
  }
  return saida
}

/** Imprime no PDA; lança o erro da impressora (sem papel, tampa aberta…). */
export function imprimirNoPda(documento: DocumentoImpressao, copias = 1) {
  const p = pda()
  if (!p) throw new Error('Este dispositivo não tem impressora.')
  const json = JSON.stringify(linhasPda(documento, p.colunas()))
  for (let i = 0; i < Math.max(1, copias); i++) {
    const erro = p.imprimir(json)
    if (erro) throw new Error(erro)
  }
}

/** Endereço do servidor a usar nos tablets: o que coincide com o IP do posto ou, senão, o da mesma rede (/24) do posto. */
export function enderecoPreferido(enderecos: string[], ipPosto: string | null): string | null {
  if (enderecos.length === 0) return null
  if (!ipPosto) return enderecos[0]
  const servidor = (e: string) => e.replace(/^https?:\/\//, '').replace(/:\d+$/, '')
  const rede = (ip: string) => ip.split('.').slice(0, 3).join('.')
  return enderecos.find((e) => servidor(e) === ipPosto) ?? enderecos.find((e) => rede(servidor(e)) === rede(ipPosto)) ?? enderecos[0]
}
