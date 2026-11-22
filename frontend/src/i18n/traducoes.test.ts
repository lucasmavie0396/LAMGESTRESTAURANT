// Garante que os textos novos dos ecrãs têm tradução inglesa em en.json.
// Lê o código das páginas (texto JSX e os atributos visíveis placeholder/title/aria-label/alt) e falha com a lista
// dos que faltam. Ao acrescentar um texto a um ecrã, acrescente também a tradução em src/i18n/en.json
// (em «textos»; se tiver partes variáveis, num «modelo» com {0}, {1}…).
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import ts from 'typescript'
import { describe, expect, it } from 'vitest'
import en from './en.json'
import { Tradutor } from './tradutor'

const RAIZ = join(__dirname, '..')
const ATRIBUTOS = new Set(['placeholder', 'title', 'aria-label', 'alt'])

/** Textos iguais nas duas línguas ou que não são frases (marcas, siglas, unidades). */
const IGUAIS = new Set([
  'LAMGest', 'LG', 'POS', 'PDA', 'NUIT', '· NUIT', 'IVA', 'MT', 'OK', 'PIN', 'QR', 'CSV', 'PDF', 'A4', 'Excel', 'Tailscale', 'Restaurant ERP',
  'ESC/POS', 'CD5220', 'EN', 'PT', 'IP', 'E-mail', 'Dashboard', 'Online', 'Offline', 'Tablets', 'Delivery', 'Take Away', 'Bar', 'bar',
  'Total', 'Total (', 'Subtotal', 'Subtotal:', 'Terminal', 'Terminal:', 'Base', 'Stock', 'Stock:', 'min', 'pts', 'mov.', 'bps,', 'bps)',
])
/** Exemplos técnicos (endereços, portas, chaves): ficam como estão. */
const TECNICOS = [/^https?:\/\//, /^COM\d+$/, /^LAMGEST1\./]

function ficheiros(pasta: string): string[] {
  return readdirSync(pasta).flatMap((n) => {
    const p = join(pasta, n)
    if (statSync(p).isDirectory()) return ficheiros(p)
    return p.endsWith('.tsx') && !p.includes('.test.') ? [p] : []
  })
}

/** Dentro de um elemento com translate="no" (nomes escritos pelos utilizadores, códigos…): não se traduz. */
function semTraducao(n: ts.Node): boolean {
  for (let p: ts.Node | undefined = n.parent; p; p = p.parent) {
    const abertura = ts.isJsxElement(p) ? p.openingElement : ts.isJsxSelfClosingElement(p) ? p : null
    if (abertura?.attributes.properties.some((a) => ts.isJsxAttribute(a) && a.name.getText() === 'translate'
      && a.initializer && ts.isStringLiteral(a.initializer) && a.initializer.text === 'no')) return true
  }
  return false
}

function textosVisiveis(): { texto: string; onde: string }[] {
  const lista: { texto: string; onde: string }[] = []
  for (const f of ficheiros(RAIZ)) {
    const fonte = ts.createSourceFile(f, readFileSync(f, 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
    const juntar = (texto: string, n: ts.Node) => {
      const t = texto.replace(/\s+/g, ' ').trim()
      if (!/[A-Za-zÀ-ÿ]{2,}/.test(t) || IGUAIS.has(t) || TECNICOS.some((r) => r.test(t)) || semTraducao(n)) return
      const { line } = fonte.getLineAndCharacterOfPosition(n.getStart())
      lista.push({ texto: t, onde: `${relative(RAIZ, f).replace(/\\/g, '/')}:${line + 1}` })
    }
    const visitar = (n: ts.Node) => {
      if (ts.isJsxText(n)) juntar(n.text, n)
      else if (ts.isJsxAttribute(n) && ATRIBUTOS.has(n.name.getText()) && n.initializer && ts.isStringLiteral(n.initializer))
        juntar(n.initializer.text, n)
      ts.forEachChild(n, visitar)
    }
    visitar(fonte)
  }
  return lista
}

describe('traduções inglesas', () => {
  it('todos os textos dos ecrãs têm tradução em en.json', () => {
    const tradutor = new Tradutor(en)
    const faltam = textosVisiveis().filter((t) => !tradutor.conhece(t.texto))
    expect(faltam.map((t) => `${t.onde}  «${t.texto}»`)).toEqual([])
  })
})
