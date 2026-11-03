// Cliente HTTP da API. A sessão é um cookie HttpOnly da mesma origem; o frontend nunca vê credenciais.

/** Erro devolvido pela API (formato ProblemDetails). */
export class ErroApi extends Error {
  readonly estado: number
  readonly titulo: string
  /** Erros por campo (respostas 400 de validação), com nomes de campo em camelCase. */
  readonly erros: Record<string, string[]>

  constructor(estado: number, titulo: string, detalhe: string, erros: Record<string, string[]> = {}) {
    super(detalhe || titulo)
    this.name = 'ErroApi'
    this.estado = estado
    this.titulo = titulo
    this.erros = erros
  }

  /** Sem resposta do servidor (rede, servidor desligado). */
  get semLigacao() {
    return this.estado === 0
  }
}

/** Evento global disparado quando a API responde 401 (sessão expirada ou terminada). */
export const EVENTO_SESSAO_EXPIRADA = 'lamgest:sessao-expirada'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

function camelCase(chave: string) {
  return chave.length > 0 ? chave[0].toLowerCase() + chave.slice(1) : chave
}

async function pedido<T>(metodo: string, url: string, corpo?: unknown, cabecalhos?: Record<string, string>): Promise<T> {
  let resposta: Response
  // FormData (ficheiros) segue como multipart: o navegador define o Content-Type com o separador.
  const formulario = corpo instanceof FormData
  try {
    resposta = await fetch(url, {
      method: metodo,
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        ...(corpo !== undefined && !formulario ? { 'Content-Type': 'application/json' } : {}),
        ...cabecalhos,
      },
      body: corpo === undefined ? undefined : formulario ? corpo : JSON.stringify(corpo),
    })
  } catch {
    throw new ErroApi(0, 'Sem ligação', 'Não foi possível contactar o servidor. Verifique a rede. A operação não foi gravada.')
  }

  if (resposta.status === 401 && !url.endsWith('/api/auth/login') && !url.endsWith('/api/auth/login-pin')) {
    window.dispatchEvent(new Event(EVENTO_SESSAO_EXPIRADA))
  }

  if (!resposta.ok) {
    let problema: ProblemDetails = {}
    try {
      problema = (await resposta.json()) as ProblemDetails
    } catch {
      // resposta sem corpo JSON (ex.: proxy sem API)
    }
    const erros = Object.fromEntries(Object.entries(problema.errors ?? {}).map(([k, v]) => [camelCase(k), v]))
    const detalhe =
      problema.detail ??
      (resposta.status === 403
        ? 'Não tem permissão para esta operação.'
        : resposta.status >= 500
          ? 'O servidor não conseguiu concluir a operação.'
          : 'Pedido recusado.')
    throw new ErroApi(resposta.status, problema.title ?? `Erro ${resposta.status}`, detalhe, erros)
  }

  if (resposta.status === 204) return undefined as T
  const texto = await resposta.text()
  return (texto ? JSON.parse(texto) : undefined) as T
}

export const api = {
  get: <T>(url: string) => pedido<T>('GET', url),
  post: <T = void>(url: string, corpo?: unknown, cabecalhos?: Record<string, string>) => pedido<T>('POST', url, corpo, cabecalhos),
  put: <T = void>(url: string, corpo?: unknown) => pedido<T>('PUT', url, corpo),
  delete: <T = void>(url: string) => pedido<T>('DELETE', url),
}

/** Constrói uma query string ignorando valores vazios. */
export function query(parametros: Record<string, string | number | boolean | undefined | null>) {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(parametros)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v))
  }
  const s = p.toString()
  return s ? `?${s}` : ''
}

/**
 * Chave para o cabeçalho Idempotency-Key: gerar uma por operação e reutilizá-la nas repetições.
 * UUID v4 com crypto.getRandomValues: o crypto.randomUUID só existe em HTTPS/localhost e os postos
 * abrem o servidor por http://IP:porta na rede local.
 */
export function novaChaveIdempotencia() {
  const b = crypto.getRandomValues(new Uint8Array(16))
  b[6] = (b[6] & 0x0f) | 0x40
  b[8] = (b[8] & 0x3f) | 0x80
  const h = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}
