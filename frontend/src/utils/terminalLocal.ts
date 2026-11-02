// Terminal associado a este computador. É uma conveniência por posto (guardada no navegador);
// todas as regras (sessão única por terminal, permissões) são validadas no servidor.

const CHAVE = 'lamgest.terminalId'

export function lerTerminalLocal(): number | null {
  try {
    const valor = window.localStorage.getItem(CHAVE)
    return valor ? Number(valor) || null : null
  } catch {
    return null
  }
}

export function guardarTerminalLocal(id: number | null) {
  try {
    if (id) window.localStorage.setItem(CHAVE, String(id))
    else window.localStorage.removeItem(CHAVE)
  } catch {
    // Armazenamento indisponível (modo privado): o terminal terá de ser escolhido de novo.
  }
}
