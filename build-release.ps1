$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')

$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$iscc = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed; ISCC.exe was not found.' }

& $iscc (Join-Path $PSScriptRoot 'installer\BatteryPilot.iss')
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }

$setup = Join-Path $PSScriptRoot 'dist\BatteryPilot-Setup-3.1.0-x64.exe'
if (-not (Test-Path -LiteralPath $setup)) { throw 'Setup output not found.' }
Get-Item -LiteralPath $setup
