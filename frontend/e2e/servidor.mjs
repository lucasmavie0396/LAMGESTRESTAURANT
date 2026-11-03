// Servidor para os testes E2E: recria a base LAMGestRestaurantDB_E2E, aplica as migrations, cria o administrador
// com uma palavra-passe aleatória (guardada só numa pasta temporária, apagada no fim) e arranca a API.
// Nunca toca na base principal: o nome da base é fixo e validado.
import { spawn, spawnSync } from 'node:child_process'
import { generateKeyPairSync, randomBytes, randomUUID, sign } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { CREDENCIAIS, PASTA_CREDENCIAIS, PORTA_API } from './comum.mjs'

const BASE = 'LAMGestRestaurantDB_E2E'
if (!BASE.endsWith('_E2E')) throw new Error('A base dos testes E2E tem de terminar em _E2E.')

const servidorSql = process.env.LAMGEST_TESTES_SQL ?? '.\\SYSON'
const ligacao = `Server=${servidorSql};Database=${BASE};Trusted_Connection=True;TrustServerCertificate=True`
const backend = resolve(import.meta.dirname, '../../backend')
const api = join(backend, 'LAMGestRestaurant.Api')
// Licenças de teste: chave própria desta execução (faz de gerador do fabricante; o ambiente E2E aceita-a em vez da real).
const chaves = generateKeyPairSync('ec', { namedCurve: 'P-256' })
const chavePublica = chaves.publicKey.export({ type: 'spki', format: 'der' }).toString('base64')
const NUIT = '400000002'
const dia = (dias) => new Date(Date.now() + dias * 86_400_000).toISOString().slice(0, 10)
function licenca(validaAte, nome = 'Restaurante E2E') {
  // Mesmo formato do LAMGestRestaurant.Licenciamento: LAMGEST1.<json base64url>.<ECDSA P-256 SHA-256 IEEE P1363 base64url>
  const dados = Buffer.from(JSON.stringify({ id: randomUUID(), nome, nuit: NUIT, emitidaEm: dia(-1), validaAte, versao: 1 }))
  const assinatura = sign('sha256', dados, { key: chaves.privateKey, dsaEncoding: 'ieee-p1363' })
  return { texto: `LAMGEST1.${dados.toString('base64url')}.${assinatura.toString('base64url')}`, dados: JSON.parse(dados.toString()) }
}

// Ambiente próprio: sem user-secrets de desenvolvimento, logo sem hipótese de usar a connection string da base principal.
const ambiente = { ...process.env, ASPNETCORE_ENVIRONMENT: 'E2E', ConnectionStrings__Default: ligacao, Licenciamento__ChavePublicaTeste: chavePublica }

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
// Licença válida instalada antes de a API arrancar (os testes começam logo); a renovada é activada por um teste no ecrã.
const actual = licenca(dia(365))
const renovada = licenca(dia(730), 'Restaurante E2E Renovado')
const sql = (t) => t.replaceAll("'", "''")
correr('sqlcmd', ['-S', servidorSql, '-E', '-b', '-d', BASE, '-Q',
  `INSERT INTO Licencas (LicencaId, NomeComercial, Nuit, EmitidaEm, ValidaAte, Texto, ActivadaEmUtc, UltimaVerificacaoUtc)
   VALUES ('${actual.dados.id}', N'${sql(actual.dados.nome)}', '${NUIT}', '${actual.dados.emitidaEm}', '${actual.dados.validaAte}', '${actual.texto}', SYSUTCDATETIME(), SYSUTCDATETIME());
   IF EXISTS (SELECT 1 FROM Configuracoes WHERE Chave = 'estabelecimento.nuit') UPDATE Configuracoes SET Valor = '${NUIT}' WHERE Chave = 'estabelecimento.nuit'
   ELSE INSERT INTO Configuracoes (Chave, Valor) VALUES ('estabelecimento.nuit', '${NUIT}');`])

mkdirSync(PASTA_CREDENCIAIS, { recursive: true })
writeFileSync(CREDENCIAIS, JSON.stringify({ utilizador: 'admin.e2e', palavraPasse, licencaRenovada: renovada.texto }))

console.log(`[e2e] API em http://localhost:${PORTA_API}`)
const processo = spawn('dotnet', ['run', '--no-build', '--no-launch-profile'], {
  cwd: api,
  stdio: 'inherit',
  env: { ...ambiente, ASPNETCORE_URLS: `http://localhost:${PORTA_API}` },
})
processo.on('exit', (codigo) => process.exit(codigo ?? 0))
for (const sinal of ['SIGINT', 'SIGTERM']) process.on(sinal, () => processo.kill())
