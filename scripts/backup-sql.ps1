<#
.SYNOPSIS
  Cópia de segurança completa da base do LAMGest Restaurant (SQL Server Express não tem SQL Server Agent).

.DESCRIPTION
  1. BACKUP DATABASE ... WITH CHECKSUM para um ficheiro novo com data e hora;
  2. RESTORE VERIFYONLY para confirmar que o ficheiro é legível;
  3. cópia opcional para uma segunda localização (disco externo ou partilha de rede);
  4. apaga apenas cópias deste script (<Base>_AAAAMMDD_HHMMSS.bak) mais antigas do que -DiasRetencao.
  Termina com código 1 se algum passo falhar (o Agendador de Tarefas mostra a falha) e regista tudo em -PastaLog.

.EXAMPLE
  .\backup-sql.ps1 -Servidor ".\SYSON" -BaseDados LAMGestRestaurantDB -CopiaPara "E:\Backups\LAMGest"
#>
[CmdletBinding()]
param(
    [string]$Servidor = '.\SYSON',
    [string]$BaseDados = 'LAMGestRestaurantDB',
    # Pasta onde o serviço do SQL Server escreve. Vazio = pasta de backups por omissão da instância.
    [string]$Pasta = '',
    [string]$CopiaPara = '',
    [ValidateRange(1, 3650)][int]$DiasRetencao = 14,
    [string]$PastaLog = (Join-Path $env:ProgramData 'LAMGestRestaurant\logs')
)

$ErrorActionPreference = 'Stop'

# Salvaguarda: este script só trata bases do LAMGest (a instância tem bases de outros sistemas).
if ($BaseDados -notmatch '^LAMGestRestaurant[A-Za-z0-9_]*$') {
    Write-Error "Base de dados '$BaseDados' recusada: o script só faz cópias de bases LAMGestRestaurant*."
    exit 1
}

New-Item -ItemType Directory -Force -Path $PastaLog | Out-Null
$ficheiroLog = Join-Path $PastaLog ("backup-{0:yyyyMM}.log" -f (Get-Date))
function Registar([string]$mensagem) {
    $linha = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $mensagem
    Write-Host $linha
    Add-Content -Path $ficheiroLog -Value $linha -Encoding UTF8
}

function Sql([string]$consulta) {
    $saida = & sqlcmd -S $Servidor -E -b -h -1 -W -Q "SET NOCOUNT ON; $consulta" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE): $($saida -join ' ')" }
    return $saida
}

try {
    if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw 'sqlcmd não encontrado no PATH.' }

    if ([string]::IsNullOrWhiteSpace($Pasta)) {
        $Pasta = (Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400))" | Select-Object -First 1).Trim()
    }
    $existe = (Sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$BaseDados'" | Select-Object -First 1).Trim()
    if ($existe -ne '1') { throw "A base '$BaseDados' não existe em $Servidor." }

    $nome = '{0}_{1:yyyyMMdd_HHmmss}.bak' -f $BaseDados, (Get-Date)
    $destino = Join-Path $Pasta $nome
    $destinoSql = $destino.Replace("'", "''")

    Registar "Início: $BaseDados em $Servidor -> $destino"
    Sql "BACKUP DATABASE [$BaseDados] TO DISK = N'$destinoSql' WITH INIT, CHECKSUM, NAME = N'$BaseDados (LAMGest)'" | Out-Null
    Sql "RESTORE VERIFYONLY FROM DISK = N'$destinoSql' WITH CHECKSUM" | Out-Null
    $tamanho = [math]::Round((Get-Item $destino).Length / 1MB, 1)
    Registar "Cópia verificada: $nome ($tamanho MB)"

    $pastas = @($Pasta)
    if (-not [string]::IsNullOrWhiteSpace($CopiaPara)) {
        New-Item -ItemType Directory -Force -Path $CopiaPara | Out-Null
        Copy-Item -Path $destino -Destination (Join-Path $CopiaPara $nome)
        Registar "Copiada para $CopiaPara"
        $pastas += $CopiaPara
    }

    # Retenção: só ficheiros com o padrão exacto deste script.
    $limite = (Get-Date).AddDays(-$DiasRetencao)
    $padrao = '^' + [regex]::Escape($BaseDados) + '_\d{8}_\d{6}\.bak$'
    foreach ($p in $pastas) {
        Get-ChildItem -Path $p -Filter "$BaseDados*.bak" -File |
            Where-Object { $_.Name -match $padrao -and $_.LastWriteTime -lt $limite } |
            ForEach-Object { Remove-Item $_.FullName; Registar "Removida (retenção $DiasRetencao dias): $($_.FullName)" }
    }

    Registar 'Concluído com sucesso.'
    exit 0
}
catch {
    Registar "ERRO: $($_.Exception.Message)"
    exit 1
}
