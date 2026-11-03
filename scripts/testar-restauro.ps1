<#
.SYNOPSIS
  Prova que uma cópia de segurança se consegue restaurar, sem tocar na base de produção.

.DESCRIPTION
  Restaura o ficheiro .bak para uma base temporária <Base>_Restauro, mostra contagens de controlo
  (pedidos, recibos, sessões de caixa, último pedido) e elimina a base temporária no fim
  (use -Manter para a deixar ficar e consultá-la).

.EXAMPLE
  .\testar-restauro.ps1 -Ficheiro "C:\...\Backup\LAMGestRestaurantDB_20261103_233000.bak"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Ficheiro,
    [string]$Servidor = '.\SYSON',
    [string]$BaseOriginal = 'LAMGestRestaurantDB',
    [switch]$Manter
)

$ErrorActionPreference = 'Stop'
if ($BaseOriginal -notmatch '^LAMGestRestaurant[A-Za-z0-9_]*$') { Write-Error "Base '$BaseOriginal' recusada."; exit 1 }
$destino = "${BaseOriginal}_Restauro"
# Salvaguarda: a base criada e eliminada aqui tem sempre o sufixo _Restauro.
if (-not $destino.EndsWith('_Restauro')) { exit 1 }

function Sql([string]$consulta, [string]$base = 'master') {
    # -s aceita um único carácter; '|' não aparece em nomes nem caminhos de ficheiros.
    $saida = & sqlcmd -S $Servidor -E -b -d $base -h -1 -W -s '|' -Q "SET NOCOUNT ON; $consulta" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE): $($saida -join ' ')" }
    return $saida
}

$ficheiroSql = $Ficheiro.Replace("'", "''")
try {
    $existe = (Sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$destino'" | Select-Object -First 1).Trim()
    if ($existe -ne '0') { throw "Já existe a base $destino. Elimine-a primeiro ou escolha outra cópia." }

    $pastaDados = (Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400))" | Select-Object -First 1).Trim()
    # Os ficheiros lógicos são renomeados para não colidirem com os da base original.
    $ficheiros = Sql "RESTORE FILELISTONLY FROM DISK = N'$ficheiroSql'" |
        Where-Object { $_ -match '\|' } |
        ForEach-Object { $c = $_ -split '\|'; [pscustomobject]@{ Logico = $c[0].Trim(); Tipo = $c[2].Trim() } }
    if (-not $ficheiros) { throw 'Não foi possível ler a lista de ficheiros da cópia.' }
    $moves = foreach ($f in $ficheiros) {
        $ext = if ($f.Tipo -eq 'L') { '_log.ldf' } else { '.mdf' }
        "MOVE N'$($f.Logico)' TO N'$(Join-Path $pastaDados ($destino + '_' + $f.Logico + $ext))'"
    }

    Write-Host "A restaurar $Ficheiro para $destino..."
    Sql "RESTORE DATABASE [$destino] FROM DISK = N'$ficheiroSql' WITH CHECKSUM, RECOVERY, $($moves -join ', ')" | Out-Null

    Write-Host 'Contagens de controlo na cópia restaurada:'
    Sql @"
SELECT 'Pedidos', CAST(COUNT(*) AS nvarchar(20)) FROM Pedidos
UNION ALL SELECT 'Recibos', CAST(COUNT(*) AS nvarchar(20)) FROM Recibos
UNION ALL SELECT 'Sessoes de caixa', CAST(COUNT(*) AS nvarchar(20)) FROM SessoesCaixa
UNION ALL SELECT 'Ultimo pedido (UTC)', ISNULL(CONVERT(nvarchar(30), MAX(CriadoEmUtc), 120), '-') FROM Pedidos
UNION ALL SELECT 'Migrations aplicadas', CAST(COUNT(*) AS nvarchar(20)) FROM __EFMigrationsHistory
"@ $destino | ForEach-Object { Write-Host ("  " + ($_ -replace '\|', ': ')) }
    Write-Host 'Restauro OK.'
}
catch {
    Write-Error "Falhou: $($_.Exception.Message)"
    exit 1
}
finally {
    if (-not $Manter) {
        & sqlcmd -S $Servidor -E -b -Q "IF DB_ID(N'$destino') IS NOT NULL BEGIN ALTER DATABASE [$destino] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$destino]; END" | Out-Null
        Write-Host "Base temporária $destino eliminada."
    }
}
