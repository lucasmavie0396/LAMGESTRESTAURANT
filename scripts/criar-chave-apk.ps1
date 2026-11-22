<#
.SYNOPSIS
  Cria a chave de assinatura da app LAMGest Venda (Android) e prepara os segredos do GitHub.

.DESCRIPTION
  Com uma chave própria, cada APK novo instala-se por cima do anterior (sem desinstalar e sem perder o emparelhamento).
  Faz-se UMA vez. A chave fica FORA do repositório (o repositório é público) e tem de ser guardada com cuidado:
  sem ela, as versões seguintes voltam a obrigar a desinstalar a app em todos os PDA.

  1. cria <Pasta>\lamgest-venda.keystore com o keytool do Java (JDK 17);
  2. grava <Pasta>\lamgest-venda.keystore.base64.txt (o conteúdo para o segredo ANDROID_KEYSTORE_BASE64);
  3. com -GitHub e o gh com sessão iniciada, grava os três segredos no repositório (a palavra-passe não aparece no ecrã).

  Depois: guarde a pasta num disco externo ou cofre de palavras-passe e apague o .base64.txt.
  A primeira versão assinada com esta chave obriga a desinstalar uma vez a app antiga (assinada com a chave de depuração).

.EXAMPLE
  .\scripts\criar-chave-apk.ps1
  .\scripts\criar-chave-apk.ps1 -Pasta D:\Chaves\LAMGest -GitHub
#>
[CmdletBinding()]
param(
    [string]$Pasta = (Join-Path $env:USERPROFILE 'LAMGest-chaves'),
    [string]$Alias = 'lamgest-venda',
    [switch]$GitHub
)

$ErrorActionPreference = 'Stop'
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$destino = [IO.Path]::GetFullPath($Pasta)
if ($destino.StartsWith($raiz, [StringComparison]::OrdinalIgnoreCase)) {
    throw "A chave não pode ficar dentro do repositório ($raiz): escolha outra pasta."
}

$keytool = Get-Command keytool -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $keytool -and $env:JAVA_HOME) { $keytool = Join-Path $env:JAVA_HOME 'bin\keytool.exe' }
if (-not $keytool -or -not (Test-Path $keytool)) { throw 'Não encontrei o keytool. Instale o JDK 17 (ex.: winget install EclipseAdoptium.Temurin.17.JDK) e abra uma consola nova.' }

New-Item -ItemType Directory -Force -Path $destino | Out-Null
$ficheiro = Join-Path $destino "$Alias.keystore"
if (Test-Path $ficheiro) { throw "Já existe $ficheiro. Não o substitua: as versões futuras do APK têm de ser assinadas com essa mesma chave." }

$s1 = Read-Host 'Palavra-passe da chave (mínimo 12 caracteres)' -AsSecureString
$s2 = Read-Host 'Repita a palavra-passe' -AsSecureString
$texto = { param($s) [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) }
$senha = & $texto $s1
if ($senha -ne (& $texto $s2)) { throw 'As palavras-passe não coincidem.' }
if ($senha.Length -lt 12) { throw 'A palavra-passe tem de ter pelo menos 12 caracteres.' }

& $keytool -genkeypair -v -keystore $ficheiro -storetype PKCS12 -alias $Alias -keyalg RSA -keysize 3072 -validity 10000 `
    -storepass $senha -keypass $senha -dname 'CN=LAMGest Venda, O=LAMGest Restaurant, C=MZ' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "O keytool falhou (código $LASTEXITCODE)." }

$base64 = Join-Path $destino "$Alias.keystore.base64.txt"
[IO.File]::WriteAllText($base64, [Convert]::ToBase64String([IO.File]::ReadAllBytes($ficheiro)))
Write-Host "Chave criada: $ficheiro" -ForegroundColor Green

if ($GitHub) {
    Get-Content $base64 -Raw | gh secret set ANDROID_KEYSTORE_BASE64 --repo (git -C $raiz remote get-url origin)
    $senha | gh secret set ANDROID_KEYSTORE_SENHA --repo (git -C $raiz remote get-url origin)
    $Alias | gh secret set ANDROID_KEYSTORE_ALIAS --repo (git -C $raiz remote get-url origin)
    Write-Host 'Segredos ANDROID_KEYSTORE_BASE64, ANDROID_KEYSTORE_SENHA e ANDROID_KEYSTORE_ALIAS gravados no GitHub.' -ForegroundColor Green
} else {
    Write-Host @"

No GitHub (Settings -> Secrets and variables -> Actions) crie:
  ANDROID_KEYSTORE_BASE64  conteúdo de $base64
  ANDROID_KEYSTORE_SENHA   a palavra-passe que escolheu
  ANDROID_KEYSTORE_ALIAS   $Alias
ou, com o gh com sessão iniciada:
  Get-Content "$base64" -Raw | gh secret set ANDROID_KEYSTORE_BASE64
  gh secret set ANDROID_KEYSTORE_SENHA      (pede a palavra-passe)
  gh secret set ANDROID_KEYSTORE_ALIAS --body $Alias
"@
}
Write-Host "Guarde $destino num disco externo ou cofre e apague $base64 quando os segredos estiverem no GitHub." -ForegroundColor Yellow
