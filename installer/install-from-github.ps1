#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Download and install LanMsg from GitHub (no manual zip step).

.EXAMPLE
  irm https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1 | iex

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install-from-github.ps1
#>
param(
    [string]$Repo = "Nuper-s-Projects/lanmsg",
    [string]$ReleaseTag = "latest"
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "[LanMsg] $msg" -ForegroundColor Cyan }

function Get-DownloadUrl {
    param([string]$Repo, [string]$Tag)

    $headers = @{ "User-Agent" = "LanMsg-Installer" }

    if ($Tag -eq "latest") {
        try {
            $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
            $asset = $release.assets | Where-Object { $_.name -eq "LanMsg-Setup.zip" } | Select-Object -First 1
            if ($asset) { return $asset.browser_download_url }
        } catch { }

        try {
            $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/latest" -Headers $headers
            $asset = $release.assets | Where-Object { $_.name -eq "LanMsg-Setup.zip" } | Select-Object -First 1
            if ($asset) { return $asset.browser_download_url }
        } catch { }
    } else {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers
        $asset = $release.assets | Where-Object { $_.name -eq "LanMsg-Setup.zip" } | Select-Object -First 1
        if ($asset) { return $asset.browser_download_url }
    }

    return $null
}

function Build-FromSource {
    param([string]$Repo, [string]$WorkDir)

    Write-Step "No release zip found. Cloning source from GitHub..."
    $git = Get-Command git -ErrorAction SilentlyContinue
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $git -or -not $dotnet) {
        throw @"
No LanMsg release download is available yet.

Do ONE of these:
  1. On GitHub: Actions -> Build and Release -> Run workflow
     Then run this script again.
  2. Install Git + .NET 6 SDK and run this script again (builds from source).
  3. Download LanMsg-Setup.zip manually from GitHub Releases.
"@
    }

    $cloneDir = Join-Path $WorkDir "src"
    & git clone --depth 1 "https://github.com/$Repo.git" $cloneDir
    Push-Location $cloneDir
    & powershell -ExecutionPolicy Bypass -File .\build-release.ps1
    Pop-Location

    $zip = Join-Path $cloneDir "dist\LanMsg-Setup.zip"
    if (-not (Test-Path $zip)) { throw "Build failed - no zip produced." }
    return $zip
}

Write-Step "LanMsg installer (from GitHub)"
Write-Step "Repository: $Repo"

$workDir = Join-Path $env:TEMP ("LanMsg-Install-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

try {
    $zipPath = Join-Path $workDir "LanMsg-Setup.zip"
    $url = Get-DownloadUrl -Repo $Repo -Tag $ReleaseTag

    if ($url) {
        Write-Step "Downloading $url ..."
        Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
    } else {
        $zipPath = Build-FromSource -Repo $Repo -WorkDir $workDir
    }

    Write-Step "Extracting..."
    $extractParent = Join-Path $workDir "pkg"
    Expand-Archive -Path $zipPath -DestinationPath $extractParent -Force

    $setupRoot = Join-Path $extractParent "LanMsg-Setup"
    if (-not (Test-Path $setupRoot)) {
        $setupRoot = Get-ChildItem $extractParent -Directory | Select-Object -First 1 -ExpandProperty FullName
    }

    $installScript = Join-Path $setupRoot "installer\install.ps1"
    if (-not (Test-Path $installScript)) { throw "Invalid package - install.ps1 not found." }

    Write-Step "Running installer..."
    & powershell -ExecutionPolicy Bypass -File $installScript
}
finally {
    Write-Step "Cleaning up temp files..."
    Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "LanMsg install finished." -ForegroundColor Green
