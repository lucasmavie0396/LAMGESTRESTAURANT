import { z } from 'zod'

// As mesmas regras da API (Identity): mín. 8 caracteres, uma minúscula e um algarismo.
// A validação definitiva é sempre a do servidor.
export const regraPalavraPasse = z
  .string()
  .min(8, 'A palavra-passe deve ter pelo menos 8 caracteres.')
  .regex(/[a-z]/, 'Deve conter pelo menos uma letra minúscula.')
  .regex(/[0-9]/, 'Deve conter pelo menos um algarismo.')

export const regraNomeUtilizador = z
  .string()
  .trim()
  .min(3, 'O nome de utilizador deve ter entre 3 e 50 caracteres.')
  .max(50, 'O nome de utilizador deve ter entre 3 e 50 caracteres.')
  .regex(/^[a-zA-Z0-9._@-]+$/, 'Use apenas letras, algarismos e . _ - @ (sem espaços).')
