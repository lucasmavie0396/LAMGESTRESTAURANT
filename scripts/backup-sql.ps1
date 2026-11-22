<#
.SYNOPSIS
  Cópia de segurança completa da base do LAMGest Restaurant (SQL Server Express não tem SQL Server Agent).

.DESCRIPTION
  1. BACKUP DATABASE ... WITH CHECKSUM para um ficheiro novo com data e hora;
  2. RESTORE VERIFYONLY para confirmar que o ficheiro é legível;
  3. cópia opcional para uma segunda localização (disco externo ou partilha de rede);
  4. apaga apenas cópias deste script (<Base>_AAAAMMDD_HHMMSS.bak) mais antigas do que -DiasRetencao;
  5. ao domingo (ou com -Manutencao Sempre): manutenção da base — índices fragmentados e estatísticas
     (o Express não tem SQL Server Agent). Uma falha aqui não invalida a cópia.
  Termina com código 1 se algum passo falhar (o Agendador de Tarefas mostra a falha) e regista tudo em -PastaLog.
  Escreve também backup-estado-<Base>.json em -PastaLog: o servidor LAMGest lê-o e avisa o administrador
  quando a cópia falha ou deixa de ser feita.

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
    [string]$PastaLog = (Join-Path $env:ProgramData 'LAMGestRestaurant\logs'),
    [ValidateSet('Auto', 'Sempre', 'Nunca')][string]$Manutencao = 'Auto'
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

# sqlcmd no PATH ou nas pastas onde costuma ser instalado (a tarefa corre como SYSTEM, com o PATH do sistema).
function EncontrarSqlcmd {
    $c = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $pastas = @(Join-Path $env:ProgramFiles 'SqlCmd\sqlcmd.exe')
    foreach ($v in '180', '170', '160', '150', '130') {
        $pastas += Join-Path $env:ProgramFiles "Microsoft SQL Server\Client SDK\ODBC\$v\Tools\Binn\SQLCMD.EXE"
        $pastas += Join-Path $env:ProgramFiles "Microsoft SQL Server\$v\Tools\Binn\SQLCMD.EXE"
    }
    return $pastas | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Sql([string]$consulta) {
    $saida = & $script:sqlcmd -S $Servidor -E -b -h -1 -W -Q "SET NOCOUNT ON; $consulta" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE): $($saida -join ' ')" }
    return $saida
}

# Estado para o servidor LAMGest (Configurações → Sistema e avisos ao administrador). Mantém a data da última cópia
# boa quando esta execução falha.
$ficheiroEstado = Join-Path $PastaLog "backup-estado-$BaseDados.json"
function GravarEstado([bool]$sucesso, [string]$mensagem, [string]$ficheiro = '', [double]$tamanhoMb = 0) {
    try {
        $agora = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        $ultima = $null
        if ($sucesso) { $ultima = $agora }
        elseif (Test-Path $ficheiroEstado) { $ultima = (Get-Content $ficheiroEstado -Raw -Encoding UTF8 | ConvertFrom-Json).UltimaCopiaUtc }
        $estado = [ordered]@{
            Base = $BaseDados; DataUtc = $agora; Sucesso = $sucesso; Mensagem = $mensagem
            UltimaCopiaUtc = $ultima; Ficheiro = $ficheiro; TamanhoMB = $tamanhoMb
        }
        [IO.File]::WriteAllText($ficheiroEstado, ($estado | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    }
    catch { Registar "Aviso: não foi possível gravar $ficheiroEstado ($($_.Exception.Message))" }
}

# Índices com fragmentação ≥ 10 % (reorganizar) ou ≥ 30 % (reconstruir) e estatísticas de todas as tabelas.
# A conta SYSTEM precisa de db_ddladmin e VIEW DATABASE STATE nesta base (o LAMGest-Setup dá-as).
function ManterBase {
    $sqlManutencao = @"
USE [$BaseDados];
DECLARE @sql nvarchar(max) = N'', @indices int = 0;
SELECT @sql = @sql + N'ALTER INDEX ' + QUOTENAME(i.name) + N' ON ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name)
    + CASE WHEN ps.avg_fragmentation_in_percent >= 30 THEN N' REBUILD;' ELSE N' REORGANIZE;' END + NCHAR(10)
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id
JOIN sys.objects o ON o.object_id = ps.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE ps.avg_fragmentation_in_percent >= 10 AND ps.page_count >= 100 AND i.index_id > 0 AND o.is_ms_shipped = 0;
SET @indices = (LEN(@sql) - LEN(REPLACE(@sql, N'ALTER INDEX ', N''))) / 12;
EXEC sp_executesql @sql;
SET @sql = N'';
SELECT @sql = @sql + N'UPDATE STATISTICS ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' + NCHAR(10)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0;
EXEC sp_executesql @sql;
SELECT @indices;
"@
    $indices = (Sql ($sqlManutencao -replace "`r?`n", ' ') | Select-Object -Last 1)
    return "$indices".Trim()
}

try {
    $script:sqlcmd = EncontrarSqlcmd
    if (-not $script:sqlcmd) { throw 'sqlcmd não encontrado (instale-o: winget install Microsoft.Sqlcmd).' }

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

    $notaManutencao = ''
    if ($Manutencao -eq 'Sempre' -or ($Manutencao -eq 'Auto' -and (Get-Date).DayOfWeek -eq 'Sunday')) {
        try {
            $indices = ManterBase
            $notaManutencao = " Manutenção: $indices índice(s) tratados e estatísticas actualizadas."
            Registar "Manutenção da base concluída ($indices índice(s) tratados; estatísticas actualizadas)."
        }
        catch {
            $notaManutencao = " Manutenção não feita: $($_.Exception.Message)"
            Registar "Aviso: manutenção da base não feita (a cópia está válida): $($_.Exception.Message)"
        }
    }

    GravarEstado $true ("Cópia verificada ($tamanho MB)." + $notaManutencao) $destino $tamanho
    Registar 'Concluído com sucesso.'
    exit 0
}
catch {
    Registar "ERRO: $($_.Exception.Message)"
    GravarEstado $false $_.Exception.Message
    exit 1
}
