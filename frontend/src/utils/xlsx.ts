// Exportação Excel (.xlsx) gerada no navegador: uma folha com cabeçalho a negrito, números como números
// (o Excel soma e formata) e texto como texto. Sem fórmulas nem macros.
import { strToU8, zipSync } from 'fflate'

export type Celula = string | number | null | undefined

function esc(texto: string) {
  return texto.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]!)
    // Caracteres de controlo não são válidos em XML.
    // eslint-disable-next-line no-control-regex
    .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, '')
}

/** Nome da coluna em Excel: 0 → A, 25 → Z, 26 → AA. */
export function coluna(indice: number) {
  let nome = ''
  for (let n = indice + 1; n > 0; n = Math.floor((n - 1) / 26)) nome = String.fromCharCode(65 + ((n - 1) % 26)) + nome
  return nome
}

function celula(valor: Celula, ref: string, estilo: number) {
  if (valor === null || valor === undefined || valor === '') return ''
  const s = estilo ? ` s="${estilo}"` : ''
  if (typeof valor === 'number' && Number.isFinite(valor)) return `<c r="${ref}"${s}><v>${valor}</v></c>`
  return `<c r="${ref}" t="inlineStr"${s}><is><t xml:space="preserve">${esc(String(valor))}</t></is></c>`
}

/** Ficheiro .xlsx (bytes) com uma folha. */
export function gerarXlsx(cabecalho: string[], linhas: Celula[][], nomeFolha = 'Relatório'): Uint8Array {
  const folha = esc(nomeFolha.replace(/[\\/?*[\]:]/g, ' ').slice(0, 31)) || 'Relatório'
  const larguras = cabecalho.map((c, i) => Math.min(60, Math.max(c.length, ...linhas.map((l) => String(l[i] ?? '').length)) + 2))
  const filas = [cabecalho, ...linhas].map((l, r) =>
    `<row r="${r + 1}">${l.map((v, c) => celula(v, `${coluna(c)}${r + 1}`, r === 0 ? 1 : typeof v === 'number' ? 2 : 0)).join('')}</row>`).join('')

  const ficheiros: Record<string, string> = {
    '[Content_Types].xml': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
      + '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>'
      + '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
      + '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
      + '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>',
    '_rels/.rels': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
      + '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
    'xl/workbook.xml': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
      + `xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="${folha}" sheetId="1" r:id="rId1"/></sheets></workbook>`,
    'xl/_rels/workbook.xml.rels': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
      + '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
      + '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>',
    // Estilos: 0 normal, 1 cabeçalho a negrito, 2 número com 2 casas e separador de milhares.
    'xl/styles.xml': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
      + '<numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00"/></numFmts>'
      + '<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>'
      + '<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>'
      + '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
      + '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
      + '<cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>'
      + '<xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>',
    'xl/worksheets/sheet1.xml': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
      // O cabeçalho fica fixo ao deslizar.
      + '<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>'
      + `<cols>${larguras.map((w, i) => `<col min="${i + 1}" max="${i + 1}" width="${w}" customWidth="1"/>`).join('')}</cols>`
      + `<sheetData>${filas}</sheetData></worksheet>`,
  }
  return zipSync(Object.fromEntries(Object.entries(ficheiros).map(([nome, xml]) => [nome, strToU8(xml)])), { level: 6 })
}

export function descarregarXlsx(nomeFicheiro: string, cabecalho: string[], linhas: Celula[][], nomeFolha?: string) {
  const bytes = gerarXlsx(cabecalho, linhas, nomeFolha)
  const blob = new Blob([bytes as BlobPart], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nomeFicheiro.endsWith('.xlsx') ? nomeFicheiro : `${nomeFicheiro}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
