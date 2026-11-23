<#
.SYNOPSIS
  Confirma uma instalação do LAMGest (servidor e/ou posto) sem alterar nada.

.DESCRIPTION
  Corre-se no fim de cada instalação de teste (máquina virtual limpa, ver docs/TESTE_INSTALACAO.md) e quando um
  cliente diz que algo deixou de funcionar. Só lê: serviço, saúde do servidor (HTTP e HTTPS), certificado,
  tarefa e estado das cópias de segurança, firewall, atalhos, posto e agente de impressão.
  Mostra uma linha por verificação (OK, AVISO ou FALHA) e termina com o código 1 se houver alguma FALHA.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File "C:\Program Files\LAMGestRestaurant\scripts\verificar-instalacao.ps1"
  .\scripts\verificar-instalacao.ps1 -Relatorio C:\Temp\verificacao.txt
#>
[CmdletBinding()]
param([string]$Relatorio)

$ErrorActionPreference = 'Stop'
$dados = Join-Path $env:ProgramData 'LAMGestRestaurant'
$resultados = New-Object System.Collections.Generic.List[object]
function Registar([string]$estado, [string]$o, [string]$detalhe) {
    $resultados.Add([pscustomobject]@{ Estado = $estado; Verificacao = $o; Detalhe = $detalhe })
}
function Ok([string]$o, [string]$d = '') { Registar 'OK' $o $d }
function Aviso([string]$o, [string]$d) { Registar 'AVISO' $o $d }
function Falha([string]$o, [string]$d) { Registar 'FALHA' $o $d }

function Saude([string]$url) {
    try {
        $r = Invoke-RestMethod -Uri $url -TimeoutSec 10 -UseBasicParsing
        return $r
    } catch {
        return $null
    }
}

# PowerShell 5.1: TLS 1.2 para o HTTPS do servidor.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function CertificadoHttps([string]$url) {
    try {
        $pedido = [Net.HttpWebRequest]::Create($url)
        $pedido.Timeout = 10000
        $resposta = $pedido.GetResponse()
        $resposta.Close()
        if ($pedido.ServicePoint.Certificate) {
            return New-Object Security.Cryptography.X509Certificates.X509Certificate2 $pedido.ServicePoint.Certificate
        }
    } catch {
        throw
    }
    return $null
}

function VerificarHttps([string]$nome, [string]$url) {
    try {
        $c = CertificadoHttps $url
        $dias = [int]($c.NotAfter - (Get-Date)).TotalDays
        if ($dias -lt 0) { Falha $nome "certificado expirado em $($c.NotAfter.ToString('dd/MM/yyyy'))" }
        elseif ($dias -lt 60) { Aviso $nome "certificado expira em $dias dias: volte a correr o Setup no servidor" }
        else { Ok $nome "$url (certificado válido até $($c.NotAfter.ToString('dd/MM/yyyy')))" }
    } catch {
        Falha $nome "$url não responde em HTTPS ou o certificado não é aceite: $($_.Exception.InnerException.Message)$($_.Exception.Message)"
    }
}

# Utilizadores comuns: o grupo Utilizadores (S-1-5-32-545) e Utilizadores autenticados / Todos.
$comuns = 'S-1-5-32-545', 'S-1-5-11', 'S-1-1-0'
function Regras([string]$pasta) {
    (Get-Acl $pasta).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object {
        $_.AccessControlType -eq 'Allow' -and $comuns -contains $_.IdentityReference.Value
    }
}
function PodeAlterar([string]$pasta) {
    $escrita = [Security.AccessControl.FileSystemRights]'CreateFiles, AppendData, WriteData, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership'
    [bool](Regras $pasta | Where-Object { ($_.FileSystemRights -band $escrita) -ne 0 })
}
function PodeLer([string]$pasta) {
    [bool](Regras $pasta | Where-Object { ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::ReadData) -ne 0 })
}

function Autoridade() {
    Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like '*LAMGest Restaurant - autoridade local*' }
}

# ---------------- Servidor ----------------
$servico = Get-Service -Name LAMGestRestaurant -ErrorAction SilentlyContinue
if ($servico) {
    $chave = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\LAMGestRestaurant"
    $conta = $chave.ObjectName
    if ($servico.Status -eq 'Running') { Ok 'Serviço LAMGestRestaurant' "em execução (conta $conta)" } else { Falha 'Serviço LAMGestRestaurant' "estado: $($servico.Status)" }
    if ($chave.Start -eq 2) { Ok 'Arranque do serviço' 'automático' } else { Aviso 'Arranque do serviço' "Start=$($chave.Start) (esperado: automático com atraso)" }
    if ($conta -ne 'NT SERVICE\LAMGestRestaurant') { Aviso 'Conta do serviço' "$conta (esperado: NT SERVICE\LAMGestRestaurant)" }

    $urls = ($chave.Environment | Where-Object { $_ -like 'ASPNETCORE_URLS=*' }) -replace '^ASPNETCORE_URLS=', ''
    $portaHttp = $null; $portaHttps = $null
    foreach ($u in ($urls -split ';')) {
        if ($u -match '^http://[^:]+:(\d+)') { $portaHttp = [int]$Matches[1] }
        if ($u -match '^https://[^:]+:(\d+)') { $portaHttps = [int]$Matches[1] }
    }
    if ($portaHttp) {
        $s = Saude "http://localhost:$portaHttp/api/saude"
        if (-not $s) { Falha 'Servidor (HTTP)' "não responde em http://localhost:$portaHttp/api/saude" }
        elseif (-not $s.baseDados) { Falha 'Servidor (HTTP)' 'responde, mas sem ligação à base de dados' }
        else { Ok 'Servidor (HTTP)' "http://localhost:$portaHttp, base de dados ligada" }
    } else { Falha 'Portas do serviço' "ASPNETCORE_URLS sem porta HTTP: $urls" }

    if ($portaHttps) {
        VerificarHttps 'Servidor (HTTPS)' "https://localhost:$portaHttps/api/saude"
        if (Autoridade) { Ok 'Autoridade local do HTTPS' 'instalada nas raízes fidedignas deste computador' } else { Falha 'Autoridade local do HTTPS' 'não está nas raízes fidedignas: volte a correr o Setup no servidor' }
        if (Test-Path (Join-Path $dados 'config\autoridade.cer')) { Ok 'Autoridade publicada para os postos' 'config\autoridade.cer' } else { Falha 'Autoridade publicada para os postos' 'falta config\autoridade.cer' }
    } else { Aviso 'Ligação cifrada (HTTPS)' 'desligada: palavras-passe e códigos passam sem cifra na rede local' }

    foreach ($p in @($portaHttp, $portaHttps) | Where-Object { $_ }) {
        $regra = Get-NetFirewallRule -DisplayName "LAMGest Restaurant (TCP $p)" -ErrorAction SilentlyContinue
        if ($regra -and $regra.Enabled -eq 'True') { Ok "Firewall (porta $p)" 'regra activa' } else { Aviso "Firewall (porta $p)" 'sem regra: os postos podem não chegar ao servidor' }
    }

    $config = Join-Path $dados 'config\servidor.json'
    if (Test-Path $config) { Ok 'Configuração protegida' $config } else { Falha 'Configuração protegida' "falta $config" }

    $tarefa = Get-ScheduledTask -TaskName 'LAMGest Restaurant - Backup SQL' -ErrorAction SilentlyContinue
    if ($tarefa) {
        $info = $tarefa | Get-ScheduledTaskInfo
        if ($info.LastRunTime -lt (Get-Date '2000-01-01')) { Aviso 'Tarefa das cópias de segurança' "agendada; ainda não correu (próxima: $($info.NextRunTime))" }
        elseif ($info.LastTaskResult -eq 0) { Ok 'Tarefa das cópias de segurança' "última execução $($info.LastRunTime) com sucesso" }
        else { Falha 'Tarefa das cópias de segurança' "última execução $($info.LastRunTime) com o código $($info.LastTaskResult)" }
    } else { Aviso 'Tarefa das cópias de segurança' 'não agendada' }
    foreach ($estado in Get-ChildItem (Join-Path $dados 'logs') -Filter 'backup-estado-*.json' -ErrorAction SilentlyContinue) {
        $e = Get-Content $estado.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($e.Sucesso) { Ok "Última cópia ($($e.Base))" "$($e.UltimaCopiaUtc) UTC, $($e.TamanhoMB) MB" } else { Falha "Última cópia ($($e.Base))" $e.Mensagem }
    }

    $exe = ($chave.ImagePath -replace '"', '')
    $configurador = Join-Path (Split-Path $exe) 'LAMGest-Configurar.exe'
    if (Test-Path $configurador) { Ok 'Configurador da base de dados' $configurador } else { Aviso 'Configurador da base de dados' "falta $configurador" }
    $apk = Join-Path (Split-Path $exe) 'android\LAMGest-Venda.apk'
    if (Test-Path $apk) { Ok 'App de venda (APK)' "$apk, $([math]::Round((Get-Item $apk).Length / 1MB, 1)) MB: descarga pelo QR em Configurações → Postos" }
    else { Aviso 'App de venda (APK)' "falta $apk`: os telemóveis não a podem descarregar do servidor (volte a correr o Setup)" }

    # Integridade: só administradores alteram a pasta do programa; a instalação não leva ficheiros de desenvolvimento.
    $pastaApp = Split-Path $exe
    foreach ($p in @($pastaApp, (Join-Path $pastaApp 'wwwroot'), (Join-Path $pastaApp 'scripts'), (Join-Path $pastaApp 'android')) | Where-Object { Test-Path $_ }) {
        if (PodeAlterar $p) { Falha "Permissões de $p" 'utilizadores comuns podem criar ou alterar ficheiros: volte a correr o Setup no servidor' }
        else { Ok "Permissões de $p" 'só administradores alteram' }
    }
    $restos = Get-ChildItem $pastaApp -Recurse -File -Include *.pdb, *.map, *.cs, *.ts, *.tsx, *.csproj, *.sln -ErrorAction SilentlyContinue
    if ($restos) { Aviso 'Ficheiros de desenvolvimento' "$($restos.Count) na pasta do programa (ex.: $($restos[0].Name)): volte a correr o Setup" }
    else { Ok 'Ficheiros de desenvolvimento' 'nenhum na pasta do programa' }
    $logs = Join-Path $dados 'logs'
    if ((Test-Path $logs) -and (PodeLer $logs)) { Aviso "Permissões de $logs" 'utilizadores comuns podem ler os registos: volte a correr o Setup' }
}

# ---------------- Posto ----------------
$registoPosto = Join-Path $dados 'posto.json'
if (Test-Path $registoPosto) {
    $posto = Get-Content $registoPosto -Raw -Encoding UTF8 | ConvertFrom-Json
    Ok 'Posto' "n.º $($posto.Numero), servidor $($posto.Servidor):$($posto.Porta)"
    $uri = [Uri]$posto.Endereco
    $base = "$($uri.Scheme)://$($uri.Authority)"
    if ($uri.Scheme -eq 'https') {
        if (Autoridade) { Ok 'Autoridade local do HTTPS' 'o posto confia no servidor' } else { Falha 'Autoridade local do HTTPS' 'o posto não confia no servidor: instale o posto de novo' }
        VerificarHttps 'Ligação ao servidor (HTTPS)' "$base/api/saude"
    } else {
        $s = Saude "$base/api/saude"
        if ($s -and $s.baseDados) { Ok 'Ligação ao servidor' $base } else { Falha 'Ligação ao servidor' "$base não responde (ou sem base de dados)" }
        Aviso 'Ligação cifrada (HTTPS)' 'o posto usa HTTP: se o servidor já tem HTTPS, instale o posto de novo'
    }
    $nome = if ($posto.Navegador -and (Split-Path $posto.Navegador -Leaf) -eq 'LAMGest-Agente.exe') { 'Programa do posto (sem navegador)' } else { 'Navegador' }
    if ($posto.Navegador -and (Test-Path $posto.Navegador)) { Ok $nome $posto.Navegador } else { Falha $nome "não encontrado: $($posto.Navegador)" }
    if ($nome -ne 'Navegador') {
        $wv = @('HKLM:\SOFTWARE\WOW6432Node', 'HKLM:\SOFTWARE', 'HKCU:\Software') | ForEach-Object {
            (Get-ItemProperty "$_\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" -ErrorAction SilentlyContinue).pv
        } | Where-Object { $_ -and $_ -ne '0.0.0.0' } | Select-Object -First 1
        if ($wv) { Ok 'Microsoft Edge WebView2' "versão $wv" } else { Falha 'Microsoft Edge WebView2' 'não instalado: o programa abre o Chrome/Edge em vez da janela própria' }
    }
    $atalho = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'LAMGest Restaurant.lnk'
    if (Test-Path $atalho) { Ok 'Atalho no menu Iniciar' $atalho } else { Falha 'Atalho no menu Iniciar' "falta $atalho" }

    $agente = Join-Path $dados 'agente.json'
    if (Test-Path $agente) {
        $a = Get-Content $agente -Raw -Encoding UTF8 | ConvertFrom-Json
        $exeAgente = Join-Path $env:ProgramFiles 'LAMGestRestaurant\Agente\LAMGest-Agente.exe'
        if (Test-Path $exeAgente) { Ok 'Agente de impressão' "$exeAgente (servidor $($a.Servidor))" } else { Falha 'Agente de impressão' "falta $exeAgente" }
        if (Get-Process -Name 'LAMGest-Agente' -ErrorAction SilentlyContinue) { Ok 'Agente em execução' '' } else { Aviso 'Agente em execução' 'não está a correr nesta sessão (arranca no início de sessão)' }
        if ($a.Servidor -notlike 'https://*' -and $uri.Scheme -eq 'https') { Aviso 'Agente de impressão' 'o agente ainda liga em HTTP: instale o posto de novo com o agente' }
    }
}

if (-not $servico -and -not (Test-Path $registoPosto)) { Falha 'Instalação' 'não encontrei o servidor nem um posto do LAMGest neste computador' }

$resultados | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$falhas = @($resultados | Where-Object Estado -eq 'FALHA').Count
$avisos = @($resultados | Where-Object Estado -eq 'AVISO').Count
Write-Host ("Resultado: {0} OK, {1} aviso(s), {2} falha(s)." -f @($resultados | Where-Object Estado -eq 'OK').Count, $avisos, $falhas)
if ($Relatorio) {
    $resultados | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Out-File -FilePath $Relatorio -Encoding utf8
    Write-Host "Relatório: $Relatorio"
}
if ($falhas -gt 0) { exit 1 }
