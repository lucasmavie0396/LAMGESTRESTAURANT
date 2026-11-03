import { rmSync } from 'node:fs'
import { PASTA_CREDENCIAIS } from './comum.mjs'

/** Apaga as credenciais temporárias do administrador de teste. */
export default function limpar() {
  rmSync(PASTA_CREDENCIAIS, { recursive: true, force: true })
}
