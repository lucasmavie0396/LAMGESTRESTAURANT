// Servidor para os testes E2E: recria a base LAMGestRestaurantDB_E2E, aplica as migrations, cria o administrador
// com uma palavra-passe aleatória (guardada só numa pasta temporária, apagada no fim) e arranca a API.
// Nunca toca na base principal: o nome da base é fixo e validado.
import { spawn, spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { CREDENCIAIS, PASTA_CREDENCIAIS, PORTA_API } from './comum.mjs'

const BASE = 'LAMGestRestaurantDB_E2E'
if (!BASE.endsWith('_E2E')) throw new Error('A base dos testes E2E tem de terminar em _E2E.')

const servidorSql = process.env.LAMGEST_TESTES_SQL ?? '.\\SYSON'
const ligacao = `Server=${servidorSql};Database=${BASE};Trusted_Connection=True;TrustServerCertificate=True`
const backend = resolve(import.meta.dirname, '../../backend')
const api = join(backend, 'LAMGestRestaurant.Api')
// Ambiente próprio: sem user-secrets de desenvolvimento, logo sem hipótese de usar a connection string da base principal.
const ambiente = { ...process.env, ASPNETCORE_ENVIRONMENT: 'E2E', ConnectionStrings__Default: ligacao }

function correr(comando, args, opcoes = {}) {
  const r = spawnSync(comando, args, { stdio: ['pipe', 'inherit', 'inherit'], env: ambiente, ...opcoes })
  if (r.status !== 0) throw new Error(`${comando} ${args.join(' ')} terminou com o código ${r.status}`)
}

console.log(`[e2e] A recriar ${BASE}…`)
correr('sqlcmd', ['-S', servidorSql, '-E', '-b', '-Q',
  `IF DB_ID(N'${BASE}') IS NOT NULL BEGIN ALTER DATABASE [${BASE}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${BASE}]; END`])
correr('dotnet', ['build', 'LAMGestRestaurant.Api', '-v', 'q', '--nologo'], { cwd: backend })
correr('dotnet', ['ef', 'database', 'update', '--no-build', '--connection', ligacao,
  '-p', 'LAMGestRestaurant.Infrastructure', '-s', 'LAMGestRestaurant.Api'], { cwd: backend })

const palavraPasse = randomBytes(9).toString('base64url') + 'a1'
console.log('[e2e] A criar o administrador de teste…')
correr('dotnet', ['run', '--no-build', '--no-launch-profile', '--', 'bootstrap-admin'], {
  cwd: api,
  input: `admin.e2e\nAdministrador E2E\n${palavraPasse}\n${palavraPasse}\n`,
})
mkdirSync(PASTA_CREDENCIAIS, { recursive: true })
writeFileSync(CREDENCIAIS, JSON.stringify({ utilizador: 'admin.e2e', palavraPasse }))

console.log(`[e2e] API em http://localhost:${PORTA_API}`)
const processo = spawn('dotnet', ['run', '--no-build', '--no-launch-profile'], {
  cwd: api,
  stdio: 'inherit',
  env: { ...ambiente, ASPNETCORE_URLS: `http://localhost:${PORTA_API}` },
})
processo.on('exit', (codigo) => process.exit(codigo ?? 0))
for (const sinal of ['SIGINT', 'SIGTERM']) process.on(sinal, () => processo.kill())
