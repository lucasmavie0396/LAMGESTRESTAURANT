// Tipos comuns (páginas, sessão, utilizadores). (reexportados por api.ts)

// Contratos da API (espelho dos DTOs do backend).

export interface Pagina<T> {
  itens: T[]
  total: number
  numeroPagina: number
  tamanhoPagina: number
}

export interface SessaoUtilizador {
  id: number
  nomeUtilizador: string
  nomeCompleto: string
  perfis: string[]
  permissoes: string[]
  deveAlterarPalavraPasse: boolean
}

export interface EstadoSaude {
  api: boolean
  baseDados: boolean
  migracoesPendentes: boolean
  horaServidorUtc: string
}

export interface UtilizadorResumo {
  id: number
  nomeUtilizador: string
  nomeCompleto: string
  activo: boolean
  deveAlterarPalavraPasse: boolean
  bloqueadoAteUtc: string | null
  ultimoLoginUtc: string | null
  perfis: string[]
  /** Tem código de entrada (PIN) para o ecrã táctil. */
  temPin: boolean
  /** Aparece como botão no ecrã de entrada táctil. */
  ecraTactil: boolean
}

/** Valida; AExpirar (≤ 15 dias); Tolerancia (expirou há ≤ 7 dias); Expirada, Inexistente e Invalida bloqueiam as gravações. */
export type EstadoLicenca = 'Valida' | 'AExpirar' | 'Tolerancia' | 'Expirada' | 'Inexistente' | 'Invalida'
export interface EstadoLicencaDto {
  estado: EstadoLicenca
  bloqueada: boolean
  mensagem: string
  nomeComercial: string | null
  nuit: string | null
  validaAte: string | null
  diasRestantes: number | null
  /** Só com o bloqueio «relógio atrasado»: o pedido a dar à LAM Company para o código de desbloqueio. */
  pedidoDesbloqueio?: string | null
}
export interface LicencaDto { licencaId: string; nomeComercial: string; nuit: string; emitidaEm: string; validaAte: string; activadaEmUtc: string; activadaPor: string | null }

/** Botão do ecrã de entrada táctil. */
export interface UtilizadorTactil { id: number; nome: string }

export interface PerfilResumo {
  id: number
  nome: string
  descricao: string | null
  sistema: boolean
  numeroUtilizadores: number
  permissoes: string[]
}

export interface PermissaoInfo {
  codigo: string
  modulo: string
  descricao: string
}
