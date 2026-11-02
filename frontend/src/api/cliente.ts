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
  try {
    resposta = await fetch(url, {
      method: metodo,
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        ...(corpo !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...cabecalhos,
      },
      body: corpo !== undefined ? JSON.stringify(corpo) : undefined,
    })
  } catch {
    throw new ErroApi(0, 'Sem ligação', 'Não foi possível contactar o servidor. Verifique a rede. A operação não foi gravada.')
  }

  if (resposta.status === 401 && !url.endsWith('/api/auth/login')) {
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

/** Chave para o cabeçalho Idempotency-Key: gerar uma por operação e reutilizá-la nas repetições. */
export function novaChaveIdempotencia() {
  return crypto.randomUUID()
}
