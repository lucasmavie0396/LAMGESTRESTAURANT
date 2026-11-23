// SQL nos testes E2E sem depender do sqlcmd (que pode não existir, ou não ligar ao LocalDB, no runner do GitHub):
// usa o cliente SQL do .NET Framework pelo Windows PowerShell, presente em qualquer Windows.
// Só para a base dos testes E2E (ou para a recriar a partir da master).
import { execFileSync } from 'node:child_process'

export const BASE_E2E = 'LAMGestRestaurantDB_E2E'
export const SERVIDOR_SQL = process.env.LAMGEST_TESTES_SQL ?? '.\\SYSON'

const SCRIPT = `
$ErrorActionPreference = 'Stop'
$c = New-Object System.Data.SqlClient.SqlConnection $env:LAMGEST_E2E_LIGACAO
$c.Open()
try {
  $cmd = $c.CreateCommand()
  $cmd.CommandTimeout = 300
  $cmd.CommandText = $env:LAMGEST_E2E_SQL
  [void]$cmd.ExecuteNonQuery()
} finally { $c.Dispose() }
`

/** Executa SQL na base E2E (ou na master, com base: 'master'). Lança erro se falhar. */
export function sqlE2E(consulta, { base = BASE_E2E } = {}) {
  if (base !== BASE_E2E && base !== 'master') throw new Error('Os testes E2E só usam a base _E2E (ou a master para a recriar).')
  const ligacao = `Server=${SERVIDOR_SQL};Database=${base};Integrated Security=True;TrustServerCertificate=True`
  execFileSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', SCRIPT], {
    env: { ...process.env, LAMGEST_E2E_LIGACAO: ligacao, LAMGEST_E2E_SQL: consulta },
    stdio: ['ignore', 'inherit', 'inherit'],
  })
}
