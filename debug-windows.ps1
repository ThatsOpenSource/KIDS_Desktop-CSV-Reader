#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the app in Debug for local development on Windows 11.

.DESCRIPTION
    Uses `dotnet run` (framework-dependent, Debug config), so rebuilds are
    fast and you don't wait on self-contained publish each cycle.

    Unhandled exceptions are written to:
        %TEMP%\kids-sanitise-crash.log

    Avalonia trace output goes to System.Diagnostics.Trace; view it with
    Sysinternals DebugView (https://learn.microsoft.com/sysinternals/downloads/debugview)
    if you need to see .LogToTrace() output.

.PARAMETER Verbose
    Passes the parameter through to dotnet run (build verbosity).
#>
param([switch]$Verbose)

$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Proj = Join-Path $Here "KidsXmlToCsv.csproj"

Write-Host "── dotnet run --project $Proj -c Debug"
if ($Verbose) {
    dotnet run --project $Proj -c Debug --verbosity normal
} else {
    dotnet run --project $Proj -c Debug
}

$crashLog = Join-Path $env:TEMP "kids-sanitise-crash.log"
if (Test-Path $crashLog) {
    Write-Host ""
    Write-Host "Crash log found at: $crashLog" -ForegroundColor Yellow
    Write-Host "Open it with:  notepad `"$crashLog`""
}
