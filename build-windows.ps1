#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds KIDS — Desktop CSV Reader for Windows 11.

.DESCRIPTION
    Produces self-contained artifacts under dist\windows\:

      folder\KIDS.exe    Folder build (all DLLs alongside). Fastest startup.
      KIDS.exe           Single-file build (all DLLs bundled). Ship this one.

    Neither requires .NET to be installed on the target machine.

.PARAMETER Rid
    Runtime identifier. Default: win-x64. Use win-arm64 for ARM64 devices.

.PARAMETER Mode
    Both (default) | Folder | SingleFile — choose which artifacts to build.

.EXAMPLE
    .\build-windows.ps1
    .\build-windows.ps1 -Rid win-arm64
    .\build-windows.ps1 -Mode SingleFile
#>
param(
    [string]$Rid = "win-x64",
    [ValidateSet("Both", "Folder", "SingleFile")]
    [string]$Mode = "Both"
)

$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Proj = Join-Path $Here "KidsXmlToCsv.csproj"
$Out  = Join-Path $Here "dist\windows"

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory -Path $Out | Out-Null

if ($Mode -in @("Both", "Folder")) {
    $FolderOut = Join-Path $Out "folder"
    Write-Host "── Folder build ($Rid) → $FolderOut"
    dotnet publish $Proj `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -o $FolderOut
}

if ($Mode -in @("Both", "SingleFile")) {
    $SingleOut = Join-Path $Out "single"
    Write-Host "── Single-file build ($Rid) → $SingleOut"
    dotnet publish $Proj `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -o $SingleOut

    $exe = Join-Path $SingleOut "KIDS.exe"
    if (Test-Path $exe) {
        Copy-Item $exe (Join-Path $Out "KIDS.exe") -Force
        Remove-Item -Recurse -Force $SingleOut
    }
}

# Post-build metadata sanity check on the shipped single-file exe
$shipped = Join-Path $Out "KIDS.exe"
if (Test-Path $shipped) {
    Write-Host ""
    Write-Host "── Metadata of $shipped"
    $info = (Get-Item $shipped).VersionInfo
    $info | Format-List `
        FileDescription, ProductName, CompanyName, LegalCopyright,
        FileVersion, ProductVersion, OriginalFilename |
        Out-String | Write-Host
}

Write-Host ""
Write-Host "Done. Outputs under $Out :"
Get-ChildItem $Out -Recurse -File |
    Select-Object @{N='Path';E={$_.FullName.Substring($Out.Length + 1)}},
                  @{N='Size (MB)';E={[math]::Round($_.Length / 1MB, 1)}} |
    Sort-Object Path |
    Format-Table -AutoSize
