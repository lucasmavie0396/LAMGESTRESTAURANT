<#
.SYNOPSIS
  Instala (ou actualiza) o LAMGest Restaurant como serviço Windows no servidor central. Correr como Administrador,
  a partir da pasta do pacote gerado por publicar.ps1.

.DESCRIPTION
  - Serviço "LAMGestRestaurant" com a conta virtual NT SERVICE\LAMGestRestaurant (sem palavra-passe);
  - login Windows dessa conta no SQL Server com db_datareader + db_datawriter na base do LAMGest
    (as migrations são aplicadas à parte pelo administrador, ver docs/INSTALACAO_LAN.md);
  - pastas em %ProgramData%\LAMGestRestaurant: chaves (sessões, só SYSTEM/Administradores/serviço) e logs;
  - variáveis de ambiente do serviço (connection string, URL, pastas);
  - regra de firewall de entrada só para redes de Domínio/Privadas;
  - arranque automático e verificação de /api/saude.

.EXAMPLE
  .\scripts\instalar-servico.ps1 -Porta 8080 -Servidor ".\SYSON" -BaseDados LAMGestRestaurantDB
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [int]$Porta = 8080,
    [string]$Servidor = '.\SYSON',
    [string]$BaseDados = 'LAMGestRestaurantDB',
    # Vazio = pasta do pacote (a pasta acima de scripts\).
    [string]$PastaAplicacao = '',
    [string]$NomeServico = 'LAMGestRestaurant'
)

$ErrorActionPreference = 'Stop'
if (-not $PastaAplicacao) { $PastaAplicacao = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
if ($BaseDados -notmatch '^LAMGestRestaurant[A-Za-z0-9_]*$') { throw "Base '$BaseDados' recusada: use a base do LAMGest." }
$exe = Join-Path $PastaAplicacao 'LAMGestRestaurant.Api.exe'
if (-not (Test-Path $exe)) { throw "Não encontrei $exe. Corra o script a partir do pacote publicado." }

$conta = "NT SERVICE\$NomeServico"
$dados = Join-Path $env:ProgramData 'LAMGestRestaurant'
$chaves = Join-Path $dados 'chaves'
$logs = Join-Path $dados 'logs'

Write-Host '== Pastas de dados'
New-Item -ItemType Directory -Force -Path $chaves, $logs | Out-Null

Write-Host '== Serviço'
$existente = Get-Service -Name $NomeServico -ErrorAction SilentlyContinue
if ($existente) {
    if ($existente.Status -ne 'Stopped') { Stop-Service $NomeServico; $existente.WaitForStatus('Stopped', '00:00:30') }
    & sc.exe config $NomeServico binPath= "`"$exe`"" start= delayed-auto obj= $conta | Out-Null
}
else {
    New-Service -Name $NomeServico -BinaryPathName "`"$exe`"" -DisplayName 'LAMGest Restaurant ERP' `
        -Description 'Servidor do LAMGest Restaurant (API e aplicação web)' -StartupType Automatic | Out-Null
    & sc.exe config $NomeServico start= delayed-auto obj= $conta | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'sc.exe config falhou.' }
# Reiniciar automaticamente se o processo falhar (5 s, 30 s, 60 s).
& sc.exe failure $NomeServico reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null

Write-Host '== Permissões das pastas'
# Chaves de sessão: sem herança; só SYSTEM, Administradores e o serviço.
& icacls $chaves /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "${conta}:(OI)(CI)M" | Out-Null
& icacls $logs /grant "${conta}:(OI)(CI)M" | Out-Null

Write-Host '== Variáveis de ambiente do serviço'
$ligacao = "Server=$Servidor;Database=$BaseDados;Trusted_Connection=True;TrustServerCertificate=True"
$variaveis = @(
    'ASPNETCORE_ENVIRONMENT=Production',
    "ASPNETCORE_URLS=http://0.0.0.0:$Porta",
    "ConnectionStrings__Default=$ligacao",
    "DataProtection__Pasta=$chaves",
    "Serilog__WriteTo__1__Args__path=$(Join-Path $logs 'lamgest-.log')"
)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$NomeServico" -Name Environment -Type MultiString -Value $variaveis

Write-Host '== Acesso ao SQL Server'
$sql = @"
IF SUSER_ID(N'$conta') IS NULL CREATE LOGIN [$conta] FROM WINDOWS;
USE [$BaseDados];
IF USER_ID(N'$conta') IS NULL CREATE USER [$conta] FOR LOGIN [$conta];
ALTER ROLE db_datareader ADD MEMBER [$conta];
ALTER ROLE db_datawriter ADD MEMBER [$conta];
"@
& sqlcmd -S $Servidor -E -b -Q $sql
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível dar acesso ao SQL Server (a base existe e as migrations foram aplicadas?).' }

Write-Host '== Firewall'
$regra = "LAMGest Restaurant (TCP $Porta)"
# Sem internet o Windows classifica a rede como publica: a regra vale em todos os perfis, mas so para a rede local.
Remove-NetFirewallRule -DisplayName $regra -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName $regra -Direction Inbound -Protocol TCP -LocalPort $Porta -Profile Any -RemoteAddress LocalSubnet -Action Allow | Out-Null

Write-Host '== Arranque'
Start-Service $NomeServico
$ok = $false
foreach ($i in 1..20) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 "http://localhost:$Porta/api/saude"
        if ($r.StatusCode -eq 200) { $ok = $true; break }
    }
    catch { }
}
if (-not $ok) { throw "O serviço não respondeu em http://localhost:$Porta/api/saude. Veja os logs em $logs." }

$ips = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -ExpandProperty IPAddress
Write-Host "`nLAMGest Restaurant a funcionar. Nos terminais abra:" -ForegroundColor Green
$ips | ForEach-Object { Write-Host "  http://${_}:$Porta" }
