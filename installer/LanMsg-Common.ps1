function Write-LanStep($msg) { Write-Host "[LanMsg] $msg" -ForegroundColor Cyan }

function Test-LanWindows {
    $os = Get-CimInstance Win32_OperatingSystem
    if ($os.BuildNumber -lt 10240) {
        throw "LanMsg requires Windows 10 or later."
    }
}

function Test-LanPublishReady($PublishRoot) {
    $svc = Join-Path $PublishRoot "Service\LanMsg.Service.exe"
    $tray = Join-Path $PublishRoot "Tray\LanMsg.Tray.exe"
    $cli = Join-Path $PublishRoot "Cli\lanmsg.exe"
    return (Test-Path $svc) -and (Test-Path $tray) -and (Test-Path $cli)
}

function Test-LanSelfContained($PublishRoot) {
    return Test-Path (Join-Path $PublishRoot ".self-contained")
}

function Ensure-LanDotNetRuntime {
    param([string]$PublishRoot)

    if (Test-LanSelfContained $PublishRoot) {
        Write-LanStep "Self-contained build detected - .NET runtime not required."
        return
    }

    Write-LanStep "Checking .NET 6 Desktop Runtime..."
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $hasDesktop = $false
    if ($dotnet) {
        $runtimes = & dotnet --list-runtimes 2>$null
        $hasDesktop = $runtimes -match "Microsoft.WindowsDesktop.App 6"
    }
    if ($hasDesktop) { return }

    Write-LanStep "Installing .NET 6 Desktop Runtime..."
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        winget install Microsoft.DotNet.DesktopRuntime.6 --accept-package-agreements --accept-source-agreements
        return
    }

    throw "Install .NET 6 Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/6.0 then run setup again."
}

function Build-LanPublish {
    param([string]$RepoRoot, [string]$PublishRoot, [switch]$SelfContained)

    $sc = if ($SelfContained) { "true" } else { "false" }
    Write-LanStep "Building LanMsg (self-contained: $sc)..."
    Push-Location $RepoRoot
    & dotnet publish src/LanMsg.Service/LanMsg.Service.csproj -c Release -r win-x64 --self-contained $sc -o (Join-Path $PublishRoot "Service")
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Service publish failed." }
    & dotnet publish src/LanMsg.Tray/LanMsg.Tray.csproj -c Release -r win-x64 --self-contained $sc -o (Join-Path $PublishRoot "Tray")
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Tray publish failed." }
    & dotnet publish src/LanMsg.Cli/LanMsg.Cli.csproj -c Release -r win-x64 --self-contained $sc -o (Join-Path $PublishRoot "Cli")
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "CLI publish failed." }
    Pop-Location

    if ($SelfContained) {
        "self-contained" | Set-Content (Join-Path $PublishRoot ".self-contained") -Encoding ASCII
    } elseif (Test-Path (Join-Path $PublishRoot ".self-contained")) {
        Remove-Item (Join-Path $PublishRoot ".self-contained") -Force
    }
}

function Remove-LanMsgExisting {
    param(
        [string]$InstallDir = "C:\Program Files\LanMsg",
        [switch]$KeepUserData
    )

    $ServiceName = "LanMsgService"
    $TaskName = "LanMsg Tray"

    $installed = (Test-Path $InstallDir) -or (Get-Service $ServiceName -ErrorAction SilentlyContinue)
    if (-not $installed) { return }

    Write-LanStep "Removing previous LanMsg installation..."

    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    Get-Process LanMsg.Tray, LanMsg.Service -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

    Remove-NetFirewallRule -DisplayName "LanMsg Service UDP 9847" -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName "LanMsg Service TCP 9848" -ErrorAction SilentlyContinue

    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        sc.exe stop $ServiceName 2>$null | Out-Null
        sc.exe delete $ServiceName 2>$null | Out-Null
        Start-Sleep -Seconds 2
    }

    $startMenu = [Environment]::GetFolderPath("CommonPrograms")
    $lanMsgMenu = Join-Path $startMenu "LanMsg"
    if (Test-Path $lanMsgMenu) { Remove-Item $lanMsgMenu -Recurse -Force -ErrorAction SilentlyContinue }

    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }

    if (-not $KeepUserData) {
        $dataDir = "$env:ProgramData\LanMsg"
        if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue }
        $userDir = Join-Path $env:LOCALAPPDATA "LanMsg"
        if (Test-Path $userDir) { Remove-Item $userDir -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Write-LanStep "Previous installation removed. Installing new version..."
}

function Install-LanMsg {
    param(
        [string]$RepoRoot,
        [string]$ScriptRoot,
        [string]$PublishRoot,
        [string]$InstallDir = "C:\Program Files\LanMsg"
    )

    $ServiceName = "LanMsgService"
    $TaskName = "LanMsg Tray"

    if (-not (Test-LanPublishReady $PublishRoot)) {
        $src = Join-Path $RepoRoot "src\LanMsg.Service\LanMsg.Service.csproj"
        if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path $src)) {
            Build-LanPublish -RepoRoot $RepoRoot -PublishRoot $PublishRoot
        } else {
            throw "Pre-built files missing. Download LanMsg-Setup.zip or install .NET SDK to build from source."
        }
    }

    Ensure-LanDotNetRuntime -PublishRoot $PublishRoot

    Remove-LanMsgExisting -InstallDir $InstallDir -KeepUserData

    Write-LanStep "Installing to $InstallDir..."
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

    Copy-Item (Join-Path $PublishRoot "Service\*") $InstallDir -Recurse -Force
    Copy-Item (Join-Path $PublishRoot "Tray\LanMsg.Tray.exe") $InstallDir -Force
    Copy-Item (Join-Path $PublishRoot "Tray\LanMsg.Tray.dll") $InstallDir -Force
    Copy-Item (Join-Path $PublishRoot "Tray\LanMsg.Tray.runtimeconfig.json") $InstallDir -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $PublishRoot "Tray\LanMsg.Tray.deps.json") $InstallDir -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $PublishRoot "Cli\lanmsg.exe") $InstallDir -Force
    Copy-Item (Join-Path $PublishRoot "Cli\lanmsg.dll") $InstallDir -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $PublishRoot "Cli\lanmsg.runtimeconfig.json") $InstallDir -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $PublishRoot "Cli\lanmsg.deps.json") $InstallDir -Force -ErrorAction SilentlyContinue
    Get-ChildItem (Join-Path $PublishRoot "Tray") -Filter "*.dll" | ForEach-Object {
        Copy-Item $_.FullName $InstallDir -Force
    }
    Get-ChildItem (Join-Path $PublishRoot "Cli") -Filter "*.dll" | ForEach-Object {
        Copy-Item $_.FullName $InstallDir -Force
    }

    $configExample = Join-Path $RepoRoot "config\appsettings.example.json"
    if (Test-Path $configExample) {
        Copy-Item $configExample (Join-Path $InstallDir "appsettings.example.json") -Force
    }

    $dataDir = "$env:ProgramData\LanMsg"
    New-Item -ItemType Directory -Force -Path $dataDir, "$dataDir\logs" | Out-Null

    Write-LanStep "Registering Windows service..."
    $serviceExe = Join-Path $InstallDir "LanMsg.Service.exe"
    $existing = Get-Service $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        sc.exe stop $ServiceName | Out-Null
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
    sc.exe create $ServiceName binPath= "`"$serviceExe`"" start= auto DisplayName= "LanMsg Service" | Out-Null
    sc.exe description $ServiceName "LanMsg LAN messaging receiver and discovery service" | Out-Null
    $sddl = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWRPWPLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;IU)(A;;RP;;;BU)(A;;RP;;;IU)S:(AU;FA;CCDCLCSWRPWRPWPLOCRSDRCWDWO;;;WD)"
    sc.exe sdset $ServiceName $sddl | Out-Null

    Write-LanStep "Adding firewall rules..."
    Remove-NetFirewallRule -DisplayName "LanMsg Service UDP 9847" -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName "LanMsg Service TCP 9848" -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName "LanMsg Service UDP 9847" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 9847 -Program $serviceExe -Profile Private | Out-Null
    New-NetFirewallRule -DisplayName "LanMsg Service TCP 9848" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 9848 -Program $serviceExe -Profile Private | Out-Null

    Write-LanStep "Creating logon scheduled task..."
    $trayExe = Join-Path $InstallDir "LanMsg.Tray.exe"
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    $action = New-ScheduledTaskAction -Execute $trayExe
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -GroupId "Users" -RunLevel Limited
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Description "Start LanMsg tray app at logon" | Out-Null

    Write-LanStep "Creating Start Menu shortcuts..."
    $startMenu = [Environment]::GetFolderPath("CommonPrograms")
    $lanMsgMenu = Join-Path $startMenu "LanMsg"
    New-Item -ItemType Directory -Force -Path $lanMsgMenu | Out-Null
    $shell = New-Object -ComObject WScript.Shell

    $lnk = $shell.CreateShortcut((Join-Path $lanMsgMenu "LanMsg Sender.lnk"))
    $lnk.TargetPath = $trayExe
    $lnk.Arguments = "--sender"
    $lnk.WorkingDirectory = $InstallDir
    $lnk.Save()

    $lnk2 = $shell.CreateShortcut((Join-Path $lanMsgMenu "LanMsg Settings.lnk"))
    $lnk2.TargetPath = $trayExe
    $lnk2.Arguments = "--settings"
    $lnk2.WorkingDirectory = $InstallDir
    $lnk2.Save()

    $lnkCli = $shell.CreateShortcut((Join-Path $lanMsgMenu "LanMsg CLI.lnk"))
    $lnkCli.TargetPath = Join-Path $InstallDir "lanmsg.exe"
    $lnkCli.WorkingDirectory = $InstallDir
    $lnkCli.Save()

    $lnk3 = $shell.CreateShortcut((Join-Path $lanMsgMenu "Uninstall LanMsg.lnk"))
    $lnk3.TargetPath = "powershell.exe"
    $lnk3.Arguments = "-ExecutionPolicy Bypass -File `"$(Join-Path $ScriptRoot 'uninstall.ps1')`""
    $lnk3.Save()

    Write-LanStep "Starting service..."
    try {
        Start-Service $ServiceName -ErrorAction Stop
    } catch {
        Write-LanStep "Service did not start. Trying sc.exe start..."
        sc.exe start $ServiceName 2>$null | Out-Null
        Start-Sleep -Seconds 2
        if ((Get-Service $ServiceName).Status -ne 'Running') {
            Write-Host "WARNING: LanMsg Service is not running. Start it with: Start-Service LanMsgService" -ForegroundColor Yellow
        }
    }
    Set-Service $ServiceName -StartupType Automatic

    Write-LanStep "Launching tray app..."
    Start-Process $trayExe

    Write-Host ""
    Write-Host "LanMsg installed successfully." -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Install LanMsg on other computers"
    Write-Host "  2. Enter the SAME LAN group code on each PC"
    Write-Host "  3. Click Test Message"
    Write-Host "  4. Start sending messages"
    Write-Host ""
    Write-Host "Install location: $InstallDir"
    Write-Host ("Logs: " + (Join-Path $dataDir "logs\service.log"))
}
