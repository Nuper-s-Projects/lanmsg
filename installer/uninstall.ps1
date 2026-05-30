#Requires -RunAsAdministrator
param(
    [switch]$RemoveData,
    [switch]$Silent
)

$ErrorActionPreference = "Stop"

$InstallDir = "C:\Program Files\LanMsg"
$ServiceName = "LanMsgService"
$TaskName = "LanMsg Tray"

function Write-Step($msg) { Write-Host "[LanMsg] $msg" -ForegroundColor Cyan }

Write-Step "Stopping LanMsg..."
Stop-Service $ServiceName -ErrorAction SilentlyContinue
Get-Process LanMsg.Tray -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Step "Removing scheduled task..."
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

Write-Step "Removing firewall rules..."
Remove-NetFirewallRule -DisplayName "LanMsg Service UDP 9847" -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "LanMsg Service TCP 9848" -ErrorAction SilentlyContinue

Write-Step "Removing Windows service..."
sc.exe stop $ServiceName 2>$null | Out-Null
sc.exe delete $ServiceName 2>$null | Out-Null

Write-Step "Removing Start Menu shortcuts..."
$startMenu = [Environment]::GetFolderPath("CommonPrograms")
$lanMsgMenu = Join-Path $startMenu "LanMsg"
if (Test-Path $lanMsgMenu) { Remove-Item $lanMsgMenu -Recurse -Force }

Write-Step "Removing program files..."
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }

if ($RemoveData) {
    $dataDir = "$env:ProgramData\LanMsg"
    if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force }
    $userDir = Join-Path $env:LOCALAPPDATA "LanMsg"
    if (Test-Path $userDir) { Remove-Item $userDir -Recurse -Force }
    Write-Step "Data removed."
} elseif (-not $Silent) {
    $removeData = Read-Host "Delete config, logs, and message history? (y/N)"
    if ($removeData -eq "y" -or $removeData -eq "Y") {
        $dataDir = "$env:ProgramData\LanMsg"
        if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force }
        $userDir = Join-Path $env:LOCALAPPDATA "LanMsg"
        if (Test-Path $userDir) { Remove-Item $userDir -Recurse -Force }
        Write-Step "Data removed."
    }
}

Write-Host "LanMsg uninstalled." -ForegroundColor Green
