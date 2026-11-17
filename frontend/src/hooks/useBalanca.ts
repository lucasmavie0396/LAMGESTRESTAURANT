import { useEffect, useState } from 'react'
import { api } from '../api/cliente'
import type { LeituraBalanca } from '../types/api'
import { emPda } from '../utils/pda'
import { lerTerminalLocal } from '../utils/terminalLocal'

/** Espera de cada pedido longo ao servidor (s): o servidor responde logo que chega um peso novo. */
const ESPERA_S = 8

/**
 * Peso da balança ligada a este posto, enquanto `activa` (ex.: diálogo do peso aberto). O pedido ao servidor faz o agente
 * do posto ler a balança; sem balança configurada a primeira resposta diz `configurada: false` e o ciclo pára.
 * Nos tablets e PDAs não há balança (devolve null). `terminal` permite usar outro posto (teste nas Configurações).
 */
export function useBalanca(activa: boolean, terminal: number | null = emPda() ? null : lerTerminalLocal()) {
  const [leitura, setLeitura] = useState<LeituraBalanca | null>(null)

  useEffect(() => {
    setLeitura(null)
    if (!activa || terminal === null) return
    let parar = false
    void (async () => {
      let versao = 0
      while (!parar) {
        try {
          const r = await api.get<LeituraBalanca | undefined>(`/api/balanca/${terminal}?versao=${versao}&espera=${versao === 0 ? 0 : ESPERA_S}`)
          if (parar) return
          if (!r) continue
          setLeitura(r)
          versao = r.versao
          if (!r.configurada) return
        } catch {
          if (parar) return
          setLeitura((x) => (x ? { ...x, peso: null, estavel: false, erro: 'Sem ligação ao servidor.' } : x))
          await new Promise((ok) => window.setTimeout(ok, 2000))
        }
      }
    })()
    return () => { parar = true }
  }, [activa, terminal])

  return leitura
}

/** Peso em kg na unidade do produto (kg ou g); null se o produto não é vendido a peso. */
export function pesoNaUnidade(pesoKg: number, unidade: string): number | null {
  if (unidade === 'kg') return Math.round(pesoKg * 1000) / 1000
  if (unidade === 'g') return Math.round(pesoKg * 1000)
  return null
}
