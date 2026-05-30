#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptRoot
$PublishRoot = Join-Path $RepoRoot "publish"

. (Join-Path $ScriptRoot "LanMsg-Common.ps1")

Write-LanStep "LanMsg setup starting..."
Test-LanWindows
Install-LanMsg -RepoRoot $RepoRoot -ScriptRoot $ScriptRoot -PublishRoot $PublishRoot
