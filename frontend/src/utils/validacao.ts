import { z } from 'zod'

// As mesmas regras da API (Identity): mín. 8 caracteres, uma minúscula e um algarismo.
// A validação definitiva é sempre a do servidor.
export const regraPalavraPasse = z
  .string()
  .min(8, 'A palavra-passe deve ter pelo menos 8 caracteres.')
  .regex(/[a-z]/, 'Deve conter pelo menos uma letra minúscula.')
  .regex(/[0-9]/, 'Deve conter pelo menos um algarismo.')

/** Código do ecrã táctil, com as regras do servidor (RegrasPin): 4 a 6 algarismos, sem todos iguais nem sequências. */
export function erroPin(pin: string): string | null {
  if (!/^\d{4,6}$/.test(pin)) return 'O código tem de ter 4 a 6 algarismos.'
  if (new Set(pin).size === 1) return 'O código não pode ter os algarismos todos iguais.'
  const passos = new Set([...pin].slice(1).map((d, i) => Number(d) - Number(pin[i])))
  if (passos.size === 1 && (passos.has(1) || passos.has(-1))) return 'O código não pode ser uma sequência (ex.: 1234).'
  return null
}

export const regraNomeUtilizador = z
  .string()
  .trim()
  .min(3, 'O nome de utilizador deve ter entre 3 e 50 caracteres.')
  .max(50, 'O nome de utilizador deve ter entre 3 e 50 caracteres.')
  .regex(/^[a-zA-Z0-9._@-]+$/, 'Use apenas letras, algarismos e . _ - @ (sem espaços).')
