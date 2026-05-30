<#
.SYNOPSIS
  Fast download + install LanMsg from GitHub.

.EXAMPLE
  irm https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1 | iex
#>
param(
    [string]$Repo = "Nuper-s-Projects/lanmsg",
    [string]$ReleaseTag = "latest",
    [ValidateSet("auto", "lite", "full")]
    [string]$Package = "auto",
    [switch]$Elevated
)

$ScriptUrl = "https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1"
$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $p = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Request-Admin {
    Write-Host ""
    Write-Host "LanMsg needs Administrator access to install." -ForegroundColor Yellow
    Write-Host "Click Yes on the UAC prompt to continue..." -ForegroundColor Yellow
    Write-Host ""

    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath) -or -not (Test-Path $scriptPath)) {
        $scriptPath = Join-Path $env:TEMP "LanMsg-install-from-github.ps1"
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            & curl.exe -fsSL -o $scriptPath $ScriptUrl
        } else {
            (Invoke-WebRequest -Uri $ScriptUrl -UseBasicParsing).Content | Set-Content $scriptPath -Encoding UTF8
        }
    }

    $psArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$scriptPath`"",
        "-Repo", $Repo,
        "-ReleaseTag", $ReleaseTag,
        "-Package", $Package,
        "-Elevated"
    )

    $proc = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList ($psArgs -join " ")
    exit $(if ($null -ne $proc.ExitCode) { $proc.ExitCode } else { 0 })
}

if (-not $Elevated -and -not (Test-IsAdmin)) {
    Request-Admin
}

function Write-Step($msg) { Write-Host "[LanMsg] $msg" -ForegroundColor Cyan }
function Get-AssetNames {
    param([string]$Package)
    switch ($Package) {
        "lite" { return @("LanMsg-Setup-lite.zip") }
        "full" { return @("LanMsg-Setup.zip") }
        default { return @("LanMsg-Setup-lite.zip", "LanMsg-Setup.zip") }
    }
}

function Get-DirectUrls {
    param([string]$Repo, [string]$Tag, [string]$AssetName)

    $urls = @()
    if ($Tag -eq "latest") {
        $urls += "https://github.com/$Repo/releases/latest/download/$AssetName"
        $urls += "https://github.com/$Repo/releases/download/latest/$AssetName"
    } else {
        $urls += "https://github.com/$Repo/releases/download/$Tag/$AssetName"
    }
    return $urls
}

function Test-RemoteFile {
    param([string]$Url)
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & curl.exe -fsI -r 0-0 --connect-timeout 8 $Url 2>$null | Out-Null
        return $LASTEXITCODE -eq 0
    }
    try {
        $r = Invoke-WebRequest -Uri $Url -Method Head -UseBasicParsing -TimeoutSec 8
        return $r.StatusCode -eq 200
    } catch { return $false }
}

function Resolve-DownloadUrl {
    param([string]$Repo, [string]$Tag, [string[]]$AssetNames)

    foreach ($name in $AssetNames) {
        foreach ($url in (Get-DirectUrls -Repo $Repo -Tag $Tag -AssetName $name)) {
            if (Test-RemoteFile $url) { return $url }
        }
    }

    $headers = @{ "User-Agent" = "LanMsg-Installer" }
    $api = if ($Tag -eq "latest") {
        "https://api.github.com/repos/$Repo/releases/latest"
    } else {
        "https://api.github.com/repos/$Repo/releases/tags/$Tag"
    }

    try {
        $release = Invoke-RestMethod -Uri $api -Headers $headers -TimeoutSec 10
        foreach ($name in $AssetNames) {
            $asset = $release.assets | Where-Object { $_.name -eq $name } | Select-Object -First 1
            if ($asset) { return $asset.browser_download_url }
        }
    } catch { }

    return $null
}

function Save-FastDownload {
    param([string]$Url, [string]$OutFile)

    Write-Step "Downloading (fast)..."
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & curl.exe -fL --retry 3 --connect-timeout 15 --compressed --progress-bar -o $OutFile $Url
        if ($LASTEXITCODE -eq 0 -and (Test-Path $OutFile)) { return }
        Remove-Item $OutFile -Force -ErrorAction SilentlyContinue
    }

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(15)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("LanMsg-Installer")
    try {
        $bytes = $client.GetByteArrayAsync($Url).GetAwaiter().GetResult()
        [System.IO.File]::WriteAllBytes($OutFile, $bytes)
    } finally {
        $client.Dispose()
    }
}

function Expand-FastZip {
    param([string]$ZipPath, [string]$DestDir)

    Write-Step "Extracting..."
    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    $tar = Get-Command tar.exe -ErrorAction SilentlyContinue
    if ($tar) {
        & tar.exe -xf $ZipPath -C $DestDir
        return
    }
    Expand-Archive -Path $ZipPath -DestinationPath $DestDir -Force
}

function Build-FromSource {
    param([string]$Repo, [string]$WorkDir)

    Write-Step "No release found. Building from GitHub source..."
    $git = Get-Command git -ErrorAction SilentlyContinue
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $git -or -not $dotnet) {
        throw "Run GitHub Actions -> Build and Release first, or install Git + .NET 6 SDK."
    }

    $cloneDir = Join-Path $WorkDir "src"
    & git clone --depth 1 "https://github.com/$Repo.git" $cloneDir
    Push-Location $cloneDir
    & powershell -ExecutionPolicy Bypass -File .\build-release.ps1
    Pop-Location

    $lite = Join-Path $cloneDir "dist\LanMsg-Setup-lite.zip"
    $full = Join-Path $cloneDir "dist\LanMsg-Setup.zip"
    if ($Package -eq "lite" -and (Test-Path $lite)) { return $lite }
    if (Test-Path $full) { return $full }
    if (Test-Path $lite) { return $lite }
    throw "Build failed."
}

Write-Step "LanMsg fast installer"
Write-Step "Repo: $Repo | package: $Package"

$workDir = Join-Path $env:TEMP ("LanMsg-Install-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

try {
    $assets = Get-AssetNames -Package $Package
    $url = Resolve-DownloadUrl -Repo $Repo -Tag $ReleaseTag -AssetNames $assets
    $zipPath = Join-Path $workDir "package.zip"

    if ($url) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Save-FastDownload -Url $url -OutFile $zipPath
        $sw.Stop()
        $mb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
        Write-Step ("Downloaded {0} MB in {1:N1}s" -f $mb, $sw.Elapsed.TotalSeconds)
    } else {
        $zipPath = Build-FromSource -Repo $Repo -WorkDir $workDir
    }

    $extractParent = Join-Path $workDir "pkg"
    Expand-FastZip -ZipPath $zipPath -DestDir $extractParent

    $setupRoot = Join-Path $extractParent "LanMsg-Setup"
    if (-not (Test-Path $setupRoot)) {
        $setupRoot = Get-ChildItem $extractParent -Directory | Select-Object -First 1 -ExpandProperty FullName
    }

    $installScript = Join-Path $setupRoot "installer\install.ps1"
    if (-not (Test-Path $installScript)) { throw "Invalid package." }

    Write-Step "Installing..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installScript
}
finally {
    Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "LanMsg install finished." -ForegroundColor Green
