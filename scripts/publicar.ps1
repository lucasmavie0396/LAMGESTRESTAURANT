<#
.SYNOPSIS
  Gera o pacote de instalação: API publicada + frontend compilado em wwwroot + SQL das migrations + scripts.

.DESCRIPTION
  Corre na máquina de desenvolvimento (precisa do .NET SDK e do Node.js). O resultado, por omissão em
  publicar\LAMGestRestaurant, copia-se para o servidor; o servidor só precisa do ASP.NET Core Runtime 9
  (Hosting Bundle) e do SQL Server. Ver docs/INSTALACAO_LAN.md.

.EXAMPLE
  .\scripts\publicar.ps1
#>
[CmdletBinding()]
param(
    # Vazio = publicar\LAMGestRestaurant na raiz do repositório.
    [string]$Saida = ''
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot não está disponível nos valores por omissão dos parâmetros no Windows PowerShell 5.1.
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Saida) { $Saida = Join-Path $raiz 'publicar\LAMGestRestaurant' }
$backend = Join-Path $raiz 'backend'
$frontend = Join-Path $raiz 'frontend'

function Passo([string]$texto) { Write-Host "`n== $texto" -ForegroundColor Cyan }
function Verificar([string]$o) { if ($LASTEXITCODE -ne 0) { throw "$o falhou (código $LASTEXITCODE)." } }

if (Test-Path $Saida) {
    # Só se apaga uma pasta que seja claramente um pacote anterior deste script.
    if (-not (Test-Path (Join-Path $Saida 'LAMGestRestaurant.Api.dll'))) { throw "A pasta $Saida existe e não parece um pacote do LAMGest. Escolha outra -Saida." }
    Remove-Item -Recurse -Force $Saida
}

Passo 'Frontend: dependências e compilação'
Push-Location $frontend
try {
    npm ci --no-audit --no-fund; Verificar 'npm ci'
    npm run build; Verificar 'npm run build'
}
finally { Pop-Location }

Passo 'Backend: publicação (Release)'
dotnet publish (Join-Path $backend 'LAMGestRestaurant.Api') -c Release -o $Saida --nologo; Verificar 'dotnet publish'
Remove-Item (Join-Path $Saida 'appsettings.Development.json') -ErrorAction SilentlyContinue

Passo 'Frontend -> wwwroot'
$wwwroot = Join-Path $Saida 'wwwroot'
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $frontend 'dist\*') -Destination $wwwroot -Recurse -Force

Passo 'SQL das migrations (idempotente)'
Push-Location $backend
try {
    dotnet tool restore | Out-Null; Verificar 'dotnet tool restore'
    dotnet ef migrations script --idempotent -p LAMGestRestaurant.Infrastructure -s LAMGestRestaurant.Api -o (Join-Path $Saida 'migracoes.sql'); Verificar 'dotnet ef migrations script'
}
finally { Pop-Location }

Passo 'Scripts de instalação, backup e documentação'
$destScripts = Join-Path $Saida 'scripts'
New-Item -ItemType Directory -Force -Path $destScripts | Out-Null
'instalar-servico.ps1', 'backup-sql.ps1', 'registar-backup.ps1', 'testar-restauro.ps1' |
    ForEach-Object { Copy-Item (Join-Path $PSScriptRoot $_) $destScripts }
$destDocs = Join-Path $Saida 'docs'
New-Item -ItemType Directory -Force -Path $destDocs | Out-Null
'INSTALACAO_LAN.md', 'BACKUP.md', 'PRODUCAO.md' | ForEach-Object {
    $doc = Join-Path $raiz "docs\$_"
    if (Test-Path $doc) { Copy-Item $doc $destDocs }
}
Copy-Item (Join-Path $raiz '.env.example') $Saida

$versao = (git -C $raiz rev-parse --short HEAD 2>$null)
Set-Content -Path (Join-Path $Saida 'VERSAO.txt') -Value ("Commit: {0}`r`nGerado: {1:yyyy-MM-dd HH:mm}" -f $versao, (Get-Date)) -Encoding UTF8

Write-Host "`nPacote pronto em $(Resolve-Path $Saida)" -ForegroundColor Green
