// Tradução do ecrã PT → EN.
// As páginas são escritas em Português; em Inglês, este tradutor troca no ecrã os textos que estão no dicionário
// (texto exacto ou modelo com partes variáveis, ex.: «Pedido {0} estornado.»). Texto sem tradução fica em Português.
// Inclui as mensagens que vêm da API. Os documentos impressos não passam por aqui (ficam no idioma da empresa).
// Para não traduzir uma zona (ex.: nomes escritos pelo utilizador), marcar com translate="no".

export type Dicionario = { textos: Record<string, string>; modelos: Record<string, string> }

interface Modelo {
  re: RegExp
  en: string
}

const ATRIBUTOS = ['placeholder', 'title', 'aria-label', 'alt']
const SEM_TEXTO = new Set(['SCRIPT', 'STYLE', 'TEXTAREA'])

export class Tradutor {
  private readonly textos: Map<string, string>
  private readonly modelos: Modelo[]
  private readonly cache = new Map<string, string>()
  /** Último valor que este tradutor escreveu em cada nó (para não traduzir duas vezes). */
  private readonly escrito = new WeakMap<Node, string>()
  private readonly attrsEscritos = new WeakMap<Element, Map<string, string>>()
  private observador: MutationObserver | null = null

  constructor(dic: Dicionario) {
    this.textos = new Map(Object.entries(dic.textos).map(([pt, en]) => [normalizar(pt), en]))
    this.modelos = Object.entries(dic.modelos)
      .map(([pt, en]) => ({ pt: normalizar(pt), en }))
      // Os mais específicos (mais texto fixo) primeiro.
      .sort((a, b) => fixo(b.pt) - fixo(a.pt))
      .map(({ pt, en }) => ({ re: paraRegex(pt), en }))
  }

  /** Tradução de um texto (mantém os espaços à volta). Devolve o próprio texto quando não há tradução. */
  traduzir(texto: string): string {
    if (!texto || !/[A-Za-zÀ-ÿ]/.test(texto)) return texto
    const guardado = this.cache.get(texto)
    if (guardado !== undefined) return guardado
    const inicio = texto.match(/^\s*/)![0]
    const fim = texto.slice(inicio.length).match(/\s*$/)![0]
    const nucleo = normalizar(texto)
    const traduzido = this.nucleo(nucleo, 0)
    const resultado = traduzido === nucleo ? texto : inicio + traduzido + fim
    if (this.cache.size > 20000) this.cache.clear()
    this.cache.set(texto, resultado)
    return resultado
  }

  private nucleo(t: string, nivel: number): string {
    const exacto = this.textos.get(t)
    if (exacto !== undefined) return exacto
    if (nivel > 1) return t
    for (const m of this.modelos) {
      const r = m.re.exec(t)
      if (r) return m.en.replace(/\{(\d+)\}/g, (_, i: string) => this.nucleo(r[Number(i) + 1] ?? '', nivel + 1))
    }
    return t
  }

  /** Traduz um nó e tudo o que tem dentro. */
  traduzirNo(no: Node) {
    if (no.nodeType === Node.TEXT_NODE) {
      this.texto(no as Text)
      return
    }
    if (no.nodeType !== Node.ELEMENT_NODE) return
    const el = no as Element
    if (excluido(el)) return
    this.atributos(el)
    if (SEM_TEXTO.has(el.tagName)) return
    const caminhante = document.createTreeWalker(el, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
      acceptNode: (n) => {
        if (n.nodeType !== Node.ELEMENT_NODE) return NodeFilter.FILTER_ACCEPT
        const e = n as Element
        if (e.getAttribute('translate') === 'no') return NodeFilter.FILTER_REJECT
        // Caixas de texto: traduz o placeholder/título, nunca o que o utilizador escreveu.
        if (SEM_TEXTO.has(e.tagName)) {
          this.atributos(e)
          return NodeFilter.FILTER_REJECT
        }
        return NodeFilter.FILTER_ACCEPT
      },
    })
    let n = caminhante.nextNode()
    while (n) {
      if (n.nodeType === Node.TEXT_NODE) this.texto(n as Text)
      else this.atributos(n as Element)
      n = caminhante.nextNode()
    }
  }

  private texto(no: Text) {
    const valor = no.nodeValue ?? ''
    if (this.escrito.get(no) === valor) return
    const pai = no.parentElement
    if (pai && (SEM_TEXTO.has(pai.tagName) || excluido(pai))) return
    const novo = this.traduzir(valor)
    if (novo !== valor) {
      this.escrito.set(no, novo)
      no.nodeValue = novo
    }
  }

  private atributos(el: Element) {
    for (const a of ATRIBUTOS) {
      const valor = el.getAttribute(a)
      if (!valor) continue
      let escritos = this.attrsEscritos.get(el)
      if (escritos?.get(a) === valor) continue
      const novo = this.traduzir(valor)
      if (novo !== valor) {
        if (!escritos) this.attrsEscritos.set(el, (escritos = new Map()))
        escritos.set(a, novo)
        el.setAttribute(a, novo)
      }
    }
  }

  /** Traduz a página e as alterações seguintes (o React actualiza os textos; o tradutor volta a traduzir). */
  observar(raiz: Node = document.body) {
    this.traduzirNo(raiz)
    this.observador = new MutationObserver((mudancas) => {
      for (const m of mudancas) {
        if (m.type === 'characterData') this.traduzirNo(m.target)
        else if (m.type === 'attributes') {
          if (m.target.nodeType === Node.ELEMENT_NODE && !excluido(m.target as Element)) this.atributos(m.target as Element)
        } else m.addedNodes.forEach((n) => this.traduzirNo(n))
      }
    })
    this.observador.observe(raiz, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATRIBUTOS })
  }

  parar() {
    this.observador?.disconnect()
    this.observador = null
  }
}

function excluido(el: Element) {
  return el.closest('[translate="no"]') !== null
}

function normalizar(t: string) {
  return t.replace(/\s+/g, ' ').trim()
}

function fixo(pt: string) {
  return pt.replace(/\{\d+\}/g, '').length
}

function paraRegex(pt: string) {
  const corpo = pt
    .split(/(\{\d+\})/)
    // (.*?): uma parte variável pode vir vazia (ex.: «{0} pendentes · {1} em preparação{2}» sem atrasos).
    .map((p) => (/^\{\d+\}$/.test(p) ? '(.*?)' : p.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
    .join('')
  // Os grupos seguem a ordem do texto; {n} refere-se ao n-ésimo grupo (os modelos são gerados por ordem).
  return new RegExp(`^${corpo}$`)
}

let activo: Tradutor | null = null

/** Tradução de um texto em código (ex.: confirm/alert). Sem tradutor activo, devolve o texto. */
export function t(texto: string): string {
  return activo ? activo.traduzir(texto) : texto
}

/** Liga o tradutor à página (só em Inglês) e traduz também as janelas confirm/alert/prompt e o título. */
export function ligarTradutor(dic: Dicionario) {
  activo = new Tradutor(dic)
  document.documentElement.lang = 'en'
  const confirmar = window.confirm.bind(window)
  const avisar = window.alert.bind(window)
  const perguntar = window.prompt.bind(window)
  window.confirm = (m?: string) => confirmar(m === undefined ? m : t(String(m)))
  window.alert = (m?: unknown) => avisar(m === undefined ? m : t(String(m)))
  window.prompt = (m?: string, v?: string) => perguntar(m === undefined ? m : t(m), v)
  const titulo = document.querySelector('title')
  if (titulo) activo.observar(titulo)
  activo.observar(document.body)
  return activo
}
