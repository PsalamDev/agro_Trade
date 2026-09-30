<#
.SYNOPSIS
  Builds the backend without tripping over the running API's file locks.

.DESCRIPTION
  A running AgroTrade.Api memory-maps the DLLs in its own bin folder, so MSBuild
  cannot overwrite them and `dotnet build` fails with MSB3021/MSB3027 ("The file
  is locked by AgroTrade.Api"). Those are file-copy errors, not compile errors -
  the code compiles fine. This stops the API first, builds, and with -Start
  relaunches it, so the build always succeeds without manual steps.

.EXAMPLE
  .\build.ps1
  Stop the API, then build. Leaves the API stopped.

.EXAMPLE
  .\build.ps1 -Start
  Stop the API, build, then relaunch on http://localhost:5000.

.EXAMPLE
  .\build.ps1 -Test -Start
  Stop the API, build, run the tests, then relaunch.
#>
[CmdletBinding()]
param(
    [switch]$Start,
    [switch]$Test,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Urls = 'http://localhost:5000'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'backend/AgroTrade.slnx'
$apiDir = Join-Path $repoRoot 'backend/src/AgroTrade.Api'

# A running API holds locks on files in its own bin folder.
$running = Get-Process -Name 'AgroTrade.Api' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping AgroTrade.Api (PID $($running.Id -join ', '))..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 750   # let Windows release the handles
}

Write-Host "Building AgroTrade.slnx ($Configuration)..." -ForegroundColor Cyan
dotnet build $solution --configuration $Configuration -warnaserror --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBuild FAILED." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`nBuild succeeded - 0 warnings, 0 errors." -ForegroundColor Green

if ($Test) {
    Write-Host "`nRunning tests..." -ForegroundColor Cyan
    dotnet test $solution --configuration $Configuration --no-build --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`nTests FAILED." -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "Tests passed." -ForegroundColor Green
}

if ($Start) {
    Write-Host "Starting API on $Urls..." -ForegroundColor Cyan
    Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', $apiDir, '--no-build',
                      '--configuration', $Configuration, '--urls', $Urls `
        -WorkingDirectory $apiDir
}