# Builds a zip you can share — no source code or SDK needed on other PCs.
$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$PublishRoot = Join-Path $RepoRoot "publish"
$DistRoot = Join-Path $RepoRoot "dist"
$SetupRoot = Join-Path $DistRoot "LanMsg-Setup"
$ZipPath = Join-Path $DistRoot "LanMsg-Setup.zip"

. (Join-Path $RepoRoot "installer\LanMsg-Common.ps1")

function Remove-DirRetry {
    param([string]$Path, [int]$Retries = 5)
    if (-not (Test-Path $Path)) { return $true }
    for ($i = 0; $i -lt $Retries; $i++) {
        try {
            Remove-Item $Path -Recurse -Force -ErrorAction Stop
            return $true
        } catch {
            if ($i -lt $Retries - 1) { Start-Sleep -Milliseconds 800 }
        }
    }
    return $false
}

function New-StageDir {
    param([string]$Preferred)
    if (Remove-DirRetry $Preferred) { return $Preferred }
    $fallback = Join-Path $DistRoot ("LanMsg-Setup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    New-Item -ItemType Directory -Force -Path $fallback | Out-Null
    Write-LanStep "dist\LanMsg-Setup is in use (close File Explorer there). Using: $fallback"
    return $fallback
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK required to build the release package. Install from https://dotnet.microsoft.com/download"
}

Write-LanStep "Building self-contained release (no .NET install needed for users)..."
Remove-DirRetry $PublishRoot | Out-Null
if (Test-Path $PublishRoot) {
    Write-LanStep "publish folder locked; building into publish-new..."
    $PublishRoot = Join-Path $RepoRoot "publish-new"
    Remove-DirRetry $PublishRoot | Out-Null
}
Build-LanPublish -RepoRoot $RepoRoot -PublishRoot $PublishRoot -SelfContained

Write-LanStep "Packaging LanMsg-Setup..."
New-Item -ItemType Directory -Force -Path $DistRoot | Out-Null
$StageRoot = New-StageDir $SetupRoot

New-Item -ItemType Directory -Force -Path $StageRoot, (Join-Path $StageRoot "installer"), (Join-Path $StageRoot "publish") | Out-Null

Copy-Item (Join-Path $PublishRoot "Service") (Join-Path $StageRoot "publish\Service") -Recurse -Force
Copy-Item (Join-Path $PublishRoot "Tray") (Join-Path $StageRoot "publish\Tray") -Recurse -Force
Copy-Item (Join-Path $PublishRoot ".self-contained") (Join-Path $StageRoot "publish\.self-contained") -Force
Copy-Item (Join-Path $RepoRoot "installer\install.ps1") (Join-Path $StageRoot "installer\") -Force
Copy-Item (Join-Path $RepoRoot "installer\uninstall.ps1") (Join-Path $StageRoot "installer\") -Force
Copy-Item (Join-Path $RepoRoot "installer\LanMsg-Common.ps1") (Join-Path $StageRoot "installer\") -Force

@'
@echo off
title LanMsg Setup
echo.
echo  LanMsg - LAN Messaging for Windows
echo  -----------------------------------
echo  This will ask for Administrator permission.
echo.
pause
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"\"%~dp0installer\install.ps1\"\"'"
if errorlevel 1 (
    echo.
    echo Setup failed or was cancelled.
    pause
    exit /b 1
)
echo.
echo Done! Look for the LanMsg icon in your system tray.
pause
'@ | Set-Content (Join-Path $StageRoot "Install-LanMsg.bat") -Encoding ASCII

@'
LANMSG - EASY INSTALL
=====================

1. Extract this entire folder (LanMsg-Setup)
2. Double-click  Install-LanMsg.bat
3. Click Yes when Windows asks for Administrator
4. Complete the setup wizard:
   - Pick a LAN group code (same on every PC)
   - Click Test Message
5. Use the tray icon to send messages

SHARE WITH OTHERS
-----------------
Upload  LanMsg-Setup.zip  from the dist folder
Friends only need the zip - no programming tools required.

UNINSTALL
---------
Run installer\uninstall.ps1 as Administrator
(or Start Menu -> LanMsg -> Uninstall LanMsg)
'@ | Set-Content (Join-Path $StageRoot "INSTALL.txt") -Encoding UTF8

@'
LanMsg sends popup messages between PCs on the same local network.

After install: tray icon -> Open Sender
Same group code required on all computers.
'@ | Set-Content (Join-Path $StageRoot "README.txt") -Encoding UTF8

Write-LanStep "Creating zip..."
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force -ErrorAction SilentlyContinue }
Compress-Archive -Path $StageRoot -DestinationPath $ZipPath -Force

if ($StageRoot -ne $SetupRoot) {
    Write-LanStep "Updating dist\LanMsg-Setup folder..."
    if (Remove-DirRetry $SetupRoot) {
        Copy-Item $StageRoot $SetupRoot -Recurse -Force
        if ($StageRoot -like "*LanMsg-Setup-*") { Remove-DirRetry $StageRoot | Out-Null }
    } else {
        Write-Host "Tip: Close File Explorer on dist\LanMsg-Setup and rebuild to refresh that folder." -ForegroundColor Yellow
    }
}

$sizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Release ready!" -ForegroundColor Green
Write-Host "  Zip:    $ZipPath  ($sizeMb MB)"
if (Test-Path $SetupRoot) { Write-Host "  Folder: $SetupRoot" }
Write-Host ""
Write-Host "Share LanMsg-Setup.zip with anyone. They extract and double-click Install-LanMsg.bat."
