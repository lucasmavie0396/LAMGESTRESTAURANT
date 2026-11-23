<#
.SYNOPSIS
  Gera publicar\LAMGest-Setup.exe: um único ficheiro que instala o servidor (SQL Server, base de dados, serviço)
  ou um posto de trabalho (ligação por IP e número do posto).

.DESCRIPTION
  1. compila o frontend;
  2. publica a API "self-contained" para win-x64 (o servidor não precisa de instalar o .NET) com o frontend em wwwroot
     e os scripts de cópia de segurança;
  3. embebe o pacote, a lista de migrations, a versão e o agente de impressão dos postos no instalador;
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

# Assinatura de código (opcional). Com certificado, o Windows mostra «LAMGest» como editor e o SmartScreen deixa de avisar
# (com certificado EV logo; com OV depois de alguma reputação). O certificado nunca entra no repositório:
#   LAMGEST_ASSINATURA_PFX + LAMGEST_ASSINATURA_SENHA   ficheiro .pfx e a sua palavra-passe, ou
#   LAMGEST_ASSINATURA_IMPRESSAO                        impressão digital de um certificado já instalado (ex.: token USB);
#   LAMGEST_ASSINATURA_TEMPO                            servidor de carimbo temporal (por omissão o da DigiCert);
#   LAMGEST_SIGNTOOL                                    caminho do signtool.exe (por omissão procura-o no Windows SDK).
$assinar = [bool]($env:LAMGEST_ASSINATURA_PFX -or $env:LAMGEST_ASSINATURA_IMPRESSAO)
$signtool = $null
if ($assinar) {
    $signtool = if ($env:LAMGEST_SIGNTOOL) { $env:LAMGEST_SIGNTOOL } else {
        Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool -or -not (Test-Path $signtool)) { throw 'Assinatura pedida, mas não encontrei o signtool.exe. Instale o Windows SDK ou indique LAMGEST_SIGNTOOL.' }
}
function Assinar([string]$ficheiro) {
    if (-not $assinar) { return }
    $tempo = if ($env:LAMGEST_ASSINATURA_TEMPO) { $env:LAMGEST_ASSINATURA_TEMPO } else { 'http://timestamp.digicert.com' }
    $argumentos = @('sign', '/fd', 'SHA256', '/tr', $tempo, '/td', 'SHA256', '/d', 'LAMGest Restaurant')
    if ($env:LAMGEST_ASSINATURA_PFX) { $argumentos += @('/f', $env:LAMGEST_ASSINATURA_PFX, '/p', $env:LAMGEST_ASSINATURA_SENHA) }
    else { $argumentos += @('/sha1', $env:LAMGEST_ASSINATURA_IMPRESSAO) }
    & $signtool @argumentos $ficheiro | Out-Null
    Verificar "Assinatura de $(Split-Path $ficheiro -Leaf)"
    & $signtool verify /pa $ficheiro | Out-Null
    Verificar "Verificação da assinatura de $(Split-Path $ficheiro -Leaf)"
    Write-Host "Assinado: $(Split-Path $ficheiro -Leaf)"
}
Write-Host ($(if ($assinar) { "Assinatura de código: activa ($signtool)" } else { 'Assinatura de código: desligada (sem LAMGEST_ASSINATURA_PFX nem LAMGEST_ASSINATURA_IMPRESSAO); o Windows vai mostrar «editor desconhecido».' }))

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
# Sem símbolos (.pdb) e sem o caminho desta máquina dentro das DLL (PathMap): a instalação não revela a estrutura do projecto.
dotnet publish (Join-Path $backend 'LAMGestRestaurant.Api') -c Release -r win-x64 --self-contained true -o $app --nologo `
    -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$backend=LAMGestRestaurant"; Verificar 'dotnet publish (API)'
Remove-Item (Join-Path $app 'appsettings.Development.json') -ErrorAction SilentlyContinue
Assinar (Join-Path $app 'LAMGestRestaurant.Api.exe')
New-Item -ItemType Directory -Force -Path (Join-Path $app 'wwwroot') | Out-Null
Copy-Item -Path (Join-Path $frontend 'dist\*') -Destination (Join-Path $app 'wwwroot') -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $app 'scripts') | Out-Null
'backup-sql.ps1', 'testar-restauro.ps1', 'verificar-instalacao.ps1' | ForEach-Object { Copy-Item (Join-Path $PSScriptRoot $_) (Join-Path $app 'scripts') }
$restos = Get-ChildItem $app -Recurse -File -Include *.pdb, *.map, *.cs, *.ts, *.tsx, *.csproj, *.sln, appsettings.Development.json
if ($restos) { throw "A publicação tem ficheiros de desenvolvimento: $($restos.Name -join ', ')" }
$caminho = [regex]::Escape($backend)
$comCaminho = Get-ChildItem $app -File -Filter 'LAMGestRestaurant*.dll' | Where-Object { [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($_.FullName)) -match $caminho }
if ($comCaminho) { throw "DLL com o caminho desta máquina: $($comCaminho.Name -join ', ')" }
# App de venda (Android): o servidor serve-a aos telemóveis pelo QR de Configurações → Postos.
# É a APK compilada pelo GitHub (release app-venda), descarregada para publicar\ depois de cada alteração em android\.
$apk = Join-Path $saida 'LAMGest-Venda.apk'
if (-not (Test-Path $apk) -or (Get-Item $apk).Length -lt 100KB) {
    throw "Falta $apk. Descarregue-a de https://github.com/lucasmavie0396/LAMGESTRESTAURANT/releases/download/app-venda/LAMGest-Venda.apk"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipApk = [IO.Compression.ZipFile]::OpenRead($apk)
try { if (-not $zipApk.GetEntry('AndroidManifest.xml')) { throw "$apk não é uma APK (sem AndroidManifest.xml)." } }
finally { $zipApk.Dispose() }
New-Item -ItemType Directory -Force -Path (Join-Path $app 'android') | Out-Null
Copy-Item $apk (Join-Path $app 'android\LAMGest-Venda.apk') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $app 'docs') | Out-Null
'INSTALACAO_LAN.md', 'BACKUP.md', 'PRODUCAO.md', 'SETUP.md' | ForEach-Object {
    $doc = Join-Path $raiz "docs\$_"
    if (Test-Path $doc) { Copy-Item $doc (Join-Path $app 'docs') }
}

Passo 'Configurador da base de dados (LAMGest-Configurar.exe, .NET Framework 4.7.2)'
dotnet build (Join-Path $raiz 'instalador\LAMGestRestaurant.Configurar') -c Release -o (Join-Path $temp 'configurar') --nologo; Verificar 'dotnet build (configurador)'
Assinar (Join-Path $temp 'configurar\LAMGest-Configurar.exe')
# No servidor vai com a aplicação (pasta de instalação); nos postos vai embebido no Setup.
Copy-Item (Join-Path $temp 'configurar\LAMGest-Configurar.exe') (Join-Path $app 'LAMGest-Configurar.exe') -Force
Copy-Item (Join-Path $temp 'configurar\LAMGest-Configurar.exe') (Join-Path $pacote 'LAMGest-Configurar.exe') -Force

Passo 'Pacote embebido (aplicação, migrations, versão)'
# Lista das migrations incluídas (nome dos ficheiros, sem os .Designer): o instalador compara-a com a base existente
# e o configurador, no servidor, com as bases que se podem escolher.
$utf8 = New-Object System.Text.UTF8Encoding($false)
$migracoes = Get-ChildItem (Join-Path $backend 'LAMGestRestaurant.Infrastructure\Dados\Migracoes') -Filter '*.cs' |
    Where-Object { $_.Name -match '^\d{14}_[A-Za-z0-9_]+\.cs$' } |
    ForEach-Object { $_.BaseName } | Sort-Object
[IO.File]::WriteAllText((Join-Path $pacote 'migracoes.txt'), ($migracoes -join "`n"), $utf8)
[IO.File]::WriteAllText((Join-Path $app 'migracoes.txt'), ($migracoes -join "`n"), $utf8)
Compress-Archive -Path (Join-Path $app '*') -DestinationPath (Join-Path $pacote 'LAMGestRestaurant.zip') -CompressionLevel Optimal
$commit = (git -C $raiz rev-parse --short HEAD 2>$null)
# Só conta alterações a ficheiros do repositório (ficheiros soltos não entram no pacote) e nunca o próprio instalador
# (publicar/LAMGest-Setup.exe também está no repositório, por Git LFS).
$alteracoes = if (git -C $raiz status --porcelain --untracked-files=no -- . ':!publicar' 2>$null) { '+alterações' } else { '' }
[IO.File]::WriteAllText((Join-Path $pacote 'versao.txt'), ("{0:yyyy.MM.dd} ({1}{2})" -f (Get-Date), $commit, $alteracoes), $utf8)
Write-Host ("Migrations: " + ($migracoes -join ', '))

Passo 'Agente de impressão dos postos: ficheiro único self-contained'
$versaoAgente = [IO.File]::ReadAllText((Join-Path $pacote 'versao.txt'))
dotnet publish (Join-Path $backend 'LAMGestRestaurant.Agente') -c Release -r win-x64 --self-contained true -o (Join-Path $temp 'agente') --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None `
    "-p:InformationalVersion=$versaoAgente"
Verificar 'dotnet publish (agente)'
Assinar (Join-Path $temp 'agente\LAMGest-Agente.exe')
Copy-Item (Join-Path $temp 'agente\LAMGest-Agente.exe') (Join-Path $pacote 'LAMGest-Agente.exe') -Force

Passo 'Instalador: ficheiro único self-contained'
dotnet publish $instalador -c Release -r win-x64 --self-contained true -o (Join-Path $temp 'setup') --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
Verificar 'dotnet publish (instalador)'
Assinar (Join-Path $temp 'setup\LAMGest-Setup.exe')
$setup = Join-Path $saida 'LAMGest-Setup.exe'
Copy-Item (Join-Path $temp 'setup\LAMGest-Setup.exe') $setup -Force

Remove-Item -Recurse -Force $temp
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "`nInstalador pronto: $setup ($mb MB)" -ForegroundColor Green
Write-Host 'Copie-o para o servidor e para cada posto (pen USB ou partilha de rede) e execute-o como administrador.'
