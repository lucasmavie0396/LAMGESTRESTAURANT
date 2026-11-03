import { tmpdir } from 'node:os'
import { join } from 'node:path'

export const PORTA_API = 5091
export const PORTA_WEB = 5181
export const PASTA_CREDENCIAIS = join(tmpdir(), 'lamgest-e2e')
export const CREDENCIAIS = join(PASTA_CREDENCIAIS, 'admin.json')
