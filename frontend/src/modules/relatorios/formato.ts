export const n3 = new Intl.NumberFormat('pt-MZ', { maximumFractionDigits: 3 })

/** Cor do valor: negativo a vermelho (perdas, faltas, resultado negativo). */
export function corSinal(valor: number) {
  return valor < 0 ? 'text-red-700' : ''
}
