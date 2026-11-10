import { useEffect } from 'react'
import { api } from '../api/cliente'
import { lerTerminalLocal } from '../utils/terminalLocal'

/** Intervalo do sinal; o servidor considera o posto «online» até 2 minutos depois do último. */
const INTERVALO_MS = 60_000

/**
 * Enquanto o sistema está aberto neste computador, avisa o servidor a cada minuto de que o posto associado
 * (atalho ?posto=N ou terminal escolhido no caixa) está a funcionar: aparece «online» em Configurações → Postos.
 * Falhas são ignoradas (o indicador de ligação já mostra quando o servidor não responde).
 */
export function usePresencaPosto(activo: boolean) {
  useEffect(() => {
    if (!activo) return
    const enviar = () => {
      const terminal = lerTerminalLocal()
      if (terminal && document.visibilityState !== 'hidden') void api.post(`/api/terminais/${terminal}/presenca`).catch(() => undefined)
    }
    enviar()
    const id = window.setInterval(enviar, INTERVALO_MS)
    document.addEventListener('visibilitychange', enviar)
    return () => {
      window.clearInterval(id)
      document.removeEventListener('visibilitychange', enviar)
    }
  }, [activo])
}
