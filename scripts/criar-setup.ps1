<#
.SYNOPSIS
  Gera publicar\LAMGest-Setup.exe: um único ficheiro que instala o servidor (SQL Server, base de dados, serviço)
  ou um posto de trabalho (ligação por IP e número do posto).

.DESCRIPTION
  1. compila o frontend;
  2. publica a API "self-contained" para win-x64 (o servidor não precisa de instalar o .NET) com o frontend em wwwroot
     e os scripts de cópia de segurança;
  3. embebe o pacote, a lista de migrations e a versão no instalador;
  4. publica o instalador num único .exe (também sem dependências).
  Corre na máquina de desenvolvimento (precisa do .NET SDK 9, Node.js e acesso ao NuGet).

.EXAMPLE
  .\scripts\criar-setup.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$backend = Join-Path $raiz 'backend'
$frontend = Join-Path $raiz 'frontend'
$instalador = Join-Path $raiz 'instalador\LAMGestRestaurant.Instalador'
$pacote = Join-Path $raiz 'instalador\pacote'
$temp = Join-Path $raiz 'publicar\_setup'
$saida = Join-Path $raiz 'publicar'

function Passo([string]$texto) { Write-Host "`n== $texto" -ForegroundColor Cyan }
function Verificar([string]$o) { if ($LASTEXITCODE -ne 0) { throw "$o falhou (código $LASTEXITCODE)." } }

foreach ($p in $temp, $pacote) { if (Test-Path $p) { Remove-Item -Recurse -Force $p } }
New-Item -ItemType Directory -Force -Path $temp, $pacote | Out-Null
$app = Join-Path $temp 'app'

Passo 'Frontend: dependências e compilação'
Push-Location $frontend
try {
    npm ci --no-audit --no-fund; Verificar 'npm ci'
    npm run build; Verificar 'npm run build'
}
finally { Pop-Location }

Passo 'API: publicação self-contained (win-x64)'
dotnet publish (Join-Path $backend 'LAMGestRestaurant.Api') -c Release -r win-x64 --self-contained true -o $app --nologo; Verificar 'dotnet publish (API)'
Remove-Item (Join-Path $app 'appsettings.Development.json') -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $app 'wwwroot') | Out-Null
Copy-Item -Path (Join-Path $frontend 'dist\*') -Destination (Join-Path $app 'wwwroot') -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $app 'scripts') | Out-Null
'backup-sql.ps1', 'testar-restauro.ps1' | ForEach-Object { Copy-Item (Join-Path $PSScriptRoot $_) (Join-Path $app 'scripts') }
New-Item -ItemType Directory -Force -Path (Join-Path $app 'docs') | Out-Null
'INSTALACAO_LAN.md', 'BACKUP.md', 'PRODUCAO.md', 'SETUP.md' | ForEach-Object {
    $doc = Join-Path $raiz "docs\$_"
    if (Test-Path $doc) { Copy-Item $doc (Join-Path $app 'docs') }
}

Passo 'Pacote embebido (aplicação, migrations, versão)'
Compress-Archive -Path (Join-Path $app '*') -DestinationPath (Join-Path $pacote 'LAMGestRestaurant.zip') -CompressionLevel Optimal
# Lista das migrations incluídas (nome dos ficheiros, sem os .Designer): o instalador compara-a com a base existente.
$utf8 = New-Object System.Text.UTF8Encoding($false)
$migracoes = Get-ChildItem (Join-Path $backend 'LAMGestRestaurant.Infrastructure\Dados\Migracoes') -Filter '*.cs' |
    Where-Object { $_.Name -match '^\d{14}_[A-Za-z0-9_]+\.cs$' } |
    ForEach-Object { $_.BaseName } | Sort-Object
[IO.File]::WriteAllText((Join-Path $pacote 'migracoes.txt'), ($migracoes -join "`n"), $utf8)
$commit = (git -C $raiz rev-parse --short HEAD 2>$null)
# Só conta alterações a ficheiros do repositório (ficheiros soltos não entram no pacote) e nunca o próprio instalador
# (publicar/LAMGest-Setup.exe também está no repositório, por Git LFS).
$alteracoes = if (git -C $raiz status --porcelain --untracked-files=no -- . ':!publicar' 2>$null) { '+alterações' } else { '' }
[IO.File]::WriteAllText((Join-Path $pacote 'versao.txt'), ("{0:yyyy.MM.dd} ({1}{2})" -f (Get-Date), $commit, $alteracoes), $utf8)
Write-Host ("Migrations: " + ($migracoes -join ', '))

Passo 'Instalador: ficheiro único self-contained'
dotnet publish $instalador -c Release -r win-x64 --self-contained true -o (Join-Path $temp 'setup') --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
Verificar 'dotnet publish (instalador)'
$setup = Join-Path $saida 'LAMGest-Setup.exe'
Copy-Item (Join-Path $temp 'setup\LAMGest-Setup.exe') $setup -Force

Remove-Item -Recurse -Force $temp
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "`nInstalador pronto: $setup ($mb MB)" -ForegroundColor Green
Write-Host 'Copie-o para o servidor e para cada posto (pen USB ou partilha de rede) e execute-o como administrador.'
