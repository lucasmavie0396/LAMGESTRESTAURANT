<#
.SYNOPSIS
  Regista no Agendador de Tarefas do Windows a cópia de segurança diária (correr como Administrador).

.EXAMPLE
  .\registar-backup.ps1 -Hora 23:30 -CopiaPara "E:\Backups\LAMGest"
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Hora = '23:30',
    [string]$Servidor = '.\SYSON',
    [string]$BaseDados = 'LAMGestRestaurantDB',
    [string]$Pasta = '',
    [string]$CopiaPara = '',
    [int]$DiasRetencao = 14,
    [string]$NomeTarefa = 'LAMGest Restaurant - Backup SQL'
)

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'backup-sql.ps1'
if (-not (Test-Path $script)) { throw "Não encontrei $script" }

$argumentos = "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Servidor `"$Servidor`" -BaseDados `"$BaseDados`" -DiasRetencao $DiasRetencao"
if ($Pasta) { $argumentos += " -Pasta `"$Pasta`"" }
if ($CopiaPara) { $argumentos += " -CopiaPara `"$CopiaPara`"" }

$accao = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argumentos
$quando = New-ScheduledTaskTrigger -Daily -At $Hora
# SYSTEM tem acesso à instância local por autenticação Windows; a tarefa corre mesmo sem sessão iniciada.
$conta = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$definicoes = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)

Register-ScheduledTask -TaskName $NomeTarefa -Action $accao -Trigger $quando -Principal $conta -Settings $definicoes -Force | Out-Null
Write-Host "Tarefa '$NomeTarefa' registada: todos os dias às $Hora."
Write-Host "Teste agora:  Start-ScheduledTask -TaskName '$NomeTarefa'  e veja o log em $env:ProgramData\LAMGestRestaurant\logs"
