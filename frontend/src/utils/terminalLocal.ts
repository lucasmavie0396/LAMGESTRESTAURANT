// Terminal associado a este computador. É uma conveniência por posto (guardada no navegador);
// todas as regras (sessão única por terminal, permissões) são validadas no servidor.

import type { PostoPublico } from '../types/api'

const CHAVE = 'lamgest.terminalId'
const CHAVE_POSTO = 'lamgest.posto'

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

/** Posto indicado no instalador deste computador. `nome` null = o número não está registado no servidor. */
export interface PostoLocal { numero: number; nome: string | null }

export function lerPostoLocal(): PostoLocal | null {
  try {
    const valor = window.localStorage.getItem(CHAVE_POSTO)
    return valor ? (JSON.parse(valor) as PostoLocal) : null
  } catch {
    return null
  }
}

function guardarPostoLocal(posto: PostoLocal) {
  try {
    window.localStorage.setItem(CHAVE_POSTO, JSON.stringify(posto))
  } catch {
    // Ver guardarTerminalLocal.
  }
}

/** Número de posto válido no endereço (?posto=N), ou null. */
export function numeroPostoDoEndereco(pesquisa: string): number | null {
  const valor = new URLSearchParams(pesquisa).get('posto')
  if (valor === null || !/^\d{1,3}$/.test(valor)) return null
  const numero = Number(valor)
  return numero >= 1 ? numero : null
}

/**
 * O atalho criado pelo instalador do posto abre o sistema com ?posto=N. Associa este navegador ao terminal
 * com esse número (consulta pública, sem sessão) e retira o parâmetro do endereço.
 * Sem servidor mantém-se a associação anterior; um número não registado fica assinalado na barra superior.
 */
export async function associarPostoDoEndereco(): Promise<void> {
  if (!new URLSearchParams(window.location.search).has('posto')) return
  const numero = numeroPostoDoEndereco(window.location.search)
  const url = new URL(window.location.href)
  url.searchParams.delete('posto')
  window.history.replaceState(window.history.state, '', url.pathname + url.search + url.hash)
  if (numero === null) return

  try {
    const resposta = await fetch(`/api/postos/${numero}`, { credentials: 'same-origin', signal: AbortSignal.timeout(4000) })
    if (resposta.ok) {
      const posto = (await resposta.json()) as PostoPublico
      guardarTerminalLocal(posto.terminalId)
      guardarPostoLocal({ numero, nome: posto.nome })
    } else if (resposta.status === 404) {
      guardarPostoLocal({ numero, nome: null })
    }
  } catch {
    // Servidor indisponível: o indicador de ligação já o mostra.
  }
}
