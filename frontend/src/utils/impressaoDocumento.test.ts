import { describe, expect, it } from 'vitest'
import type { DocumentoImpressao } from '../types/api'
import { htmlDocumento, levaLogotipo } from './impressaoDocumento'

const linha = (texto: string, direita: string | null = null) => ({ texto, direita, alinhamento: 'Esquerda' as const, negrito: false, grande: false, separador: false })

describe('documentos impressos pelo navegador', () => {
  const recibo: DocumentoImpressao = { titulo: 'Recibo', logotipo: 'A4E80mm', linhas: [linha('TOTAL', '450,50 MT'), linha('<script>')] }
  const relatorio: DocumentoImpressao = { ...recibo, logotipo: 'SoA4' }

  it('aplica a regra do logótipo por papel', () => {
    expect([levaLogotipo(recibo, 80), levaLogotipo(recibo, 210), levaLogotipo(recibo, 58)]).toEqual([true, true, false])
    expect([levaLogotipo(relatorio, 80), levaLogotipo(relatorio, 210)]).toEqual([false, true])
  })

  it('gera o papel pedido, escapa o texto e põe o logótipo só quando se aplica', () => {
    const talao = htmlDocumento(recibo, 80, '/api/imagens/abc')
    expect(talao).toContain('size: 80mm auto')
    expect(talao).toContain('<img src="/api/imagens/abc"')
    expect(talao).toContain('&lt;script&gt;')
    expect(talao).toContain('<span class="v">450,50 MT</span>')
    expect(htmlDocumento(recibo, 58, '/api/imagens/abc')).not.toContain('<img')
    expect(htmlDocumento(relatorio, 210, '/x')).toContain('size: A4')
  })
})
