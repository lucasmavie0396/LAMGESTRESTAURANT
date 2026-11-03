<#
.SYNOPSIS
  Gera publicar\LAMGest-Licencas.exe: o gerador de licenças (só para a LAM company; não vai no Setup dos clientes).

.DESCRIPTION
  Ficheiro único self-contained (não precisa do .NET instalado). No fim confirma que a chave deste computador
  (%APPDATA%\LAMGest\Licencas) é a mesma cuja chave pública está embutida no sistema
  (backend\LAMGestRestaurant.Licenciamento\chave-publica.txt): se não for, as licenças emitidas não seriam aceites.

.EXAMPLE
  .\scripts\criar-gerador-licencas.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projecto = Join-Path $raiz 'licencas\LAMGest.GeradorLicencas'
$temp = Join-Path $raiz 'publicar\_gerador'
$saida = Join-Path $raiz 'publicar\LAMGest-Licencas.exe'

if (Test-Path $temp) { Remove-Item -Recurse -Force $temp }
dotnet publish $projecto -c Release -r win-x64 --self-contained true -o $temp --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }
Copy-Item (Join-Path $temp 'LAMGest-Licencas.exe') $saida -Force
Remove-Item -Recurse -Force $temp

$embutida = (Get-Content (Join-Path $raiz 'backend\LAMGestRestaurant.Licenciamento\chave-publica.txt') -Raw).Trim()
$ficheiro = [IO.Path]::GetTempFileName()
try {
    $p = Start-Process -FilePath $saida -ArgumentList @('--chave-publica', "`"$ficheiro`"") -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        Write-Warning 'Este computador ainda não tem a chave das licenças: abra o gerador e reponha a cópia de segurança.'
    } elseif ((Get-Content $ficheiro -Raw).Trim() -ne $embutida) {
        Write-Warning 'A chave deste computador NÃO é a embutida no sistema: as licenças emitidas aqui não seriam aceites.'
    } else {
        Write-Host 'Chave deste computador = chave pública embutida no sistema.' -ForegroundColor Green
    }
} finally {
    Remove-Item $ficheiro -ErrorAction SilentlyContinue
}
$mb = [math]::Round((Get-Item $saida).Length / 1MB, 1)
Write-Host "Gerador pronto: $saida ($mb MB)" -ForegroundColor Green
