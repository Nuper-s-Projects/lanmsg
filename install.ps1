$here = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $here "installer\install-from-github.ps1") @args
