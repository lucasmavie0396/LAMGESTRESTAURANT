// Sala: zonas e mesas. (reexportados por api.ts)

// ---------- Sala ----------

export type EstadoMesa = 'Livre' | 'Ocupada' | 'Reservada' | 'Indisponivel'

export interface Zona {
  id: number
  nome: string
  ordem: number
  activo: boolean
  numeroMesas: number
}

export interface Mesa {
  id: number
  nome: string
  zonaId: number
  zonaNome: string
  capacidade: number
  estado: EstadoMesa
  ocupadaDesdeUtc: string | null
  activo: boolean
  pedidosAbertos: number
  totalAberto: number
  rowVersion: string
  /** Utilizador responsável pelo pedido aberto (quem abriu a mesa, ou a quem foi passada). */
  responsavelId?: number | null
  responsavel?: string | null
}
