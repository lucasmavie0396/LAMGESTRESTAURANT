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
}

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
