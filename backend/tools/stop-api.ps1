<#
.SYNOPSIS
  Stops any AgroTrade.Api started from this repository's build output.

.DESCRIPTION
  A running AgroTrade.Api memory-maps the managed DLLs in its own bin folder.
  When MSBuild later tries to refresh those same files the copy step fails with
  MSB3021/MSB3027 - "The file is locked by AgroTrade.Api" - which reads like a
  compile error but is only a failed file copy.

  Invoked automatically from Directory.Build.targets before every build, so a
  plain `dotnet build` succeeds without anyone having to remember to stop the API
  first. There is no MSBuild switch that makes Copy tolerate a file the CLR holds
  open for the lifetime of a process, and `dotnet run` offers no way to execute
  from somewhere other than the output folder, so stopping the process is the
  only real resolution.

  Only processes whose executable lives under this repo's src\AgroTrade.Api\bin
  are touched. A same-named process from another checkout, or a deployed copy of
  the API running elsewhere on the machine, is left alone. Does nothing at all
  when the API is not running.
#>
[CmdletBinding()]
param()

$binRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\AgroTrade.Api\bin'))
$stopped = @()

Get-Process -Name 'AgroTrade.Api' -ErrorAction SilentlyContinue | ForEach-Object {
    $path = $null
    try {
        $path = $_.Path
    }
    catch {
        return  # no access to the path, so it cannot be ours
    }

    if ($path -and $path.StartsWith($binRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $stopped += "$($_.Id) [$path]"
        Stop-Process -Id $_.Id -Force
    }
}

if ($stopped.Count -gt 0) {
    Start-Sleep -Milliseconds 750   # give Windows a moment to release the handles
    Write-Host "Stopped running AgroTrade.Api: $($stopped -join ', ')"
}