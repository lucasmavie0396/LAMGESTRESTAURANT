// Erros dos ecrãs enviados para o log do servidor (logs\lamgest-*.log), para o suporte os ver sem ir ao posto.
// Sem dados pessoais: só a mensagem, a pilha e a página. Os repetidos e o excesso não são enviados.

const enviados = new Set<string>()
const MAXIMO_POR_PAGINA = 10

/** Ruído conhecido dos navegadores, sem efeito para o utilizador. */
const IGNORAR = [/ResizeObserver loop/i, /^Script error\.?$/i]

export function reportarErro(erro: unknown, origem: string) {
  try {
    const e = erro instanceof Error ? erro : null
    const mensagem = (e ? `${e.name}: ${e.message}` : String(erro)).slice(0, 2000)
    if (!mensagem || IGNORAR.some((r) => r.test(mensagem))) return
    const chave = `${origem}|${mensagem}`
    if (enviados.has(chave) || enviados.size >= MAXIMO_POR_PAGINA) return
    enviados.add(chave)
    void fetch('/api/sistema/erros-cliente', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        mensagem,
        pilha: e?.stack?.slice(0, 8000) ?? null,
        pagina: `${location.pathname}${location.search}`.slice(0, 500),
        origem: origem.slice(0, 100),
      }),
      keepalive: true,
    }).catch(() => undefined)
  } catch {
    // Reportar nunca pode causar outro erro.
  }
}

/** Erros que escapam a tudo (código assíncrono, eventos): também vão para o log do servidor. */
export function ligarReporteGlobal() {
  window.addEventListener('error', (ev) => reportarErro(ev.error ?? ev.message, 'janela'))
  window.addEventListener('unhandledrejection', (ev) => reportarErro(ev.reason, 'promessa'))
}
