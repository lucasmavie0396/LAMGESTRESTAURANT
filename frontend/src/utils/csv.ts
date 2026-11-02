// Exportação CSV compatível com o Excel em Português: separador «;», vírgula decimal e BOM UTF-8.

type Celula = string | number | null | undefined

function formatar(c: Celula) {
  if (c === null || c === undefined) return ''
  const texto = typeof c === 'number' ? String(c).replace('.', ',') : c
  return /[;"\n\r]/.test(texto) ? `"${texto.replace(/"/g, '""')}"` : texto
}

export function gerarCsv(cabecalho: string[], linhas: Celula[][]) {
  return [cabecalho, ...linhas].map((l) => l.map(formatar).join(';')).join('\r\n')
}

export function descarregarCsv(nomeFicheiro: string, cabecalho: string[], linhas: Celula[][]) {
  const blob = new Blob(['﻿' + gerarCsv(cabecalho, linhas)], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nomeFicheiro.endsWith('.csv') ? nomeFicheiro : `${nomeFicheiro}.csv`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
