#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads a portable node.exe into source\Assets\Terminal\node\ if it isn't
    already there, so Claudium can bundle its own Node.js runtime instead of
    depending on one being installed system-wide.

.DESCRIPTION
    Only node.exe itself is kept (not npm/npx/corepack or node_modules) — the
    terminal helper only needs the runtime to execute terminal-helper.js and
    the bundled node-pty native addon under Assets\Terminal\node-pty.

    node.exe is not committed to git (see .gitignore) because of its size;
    this script is what both publish.ps1 (local dev) and the release workflow
    (CI) call to fetch it before building.
#>

$ErrorActionPreference = 'Stop'

$nodeVersion = '24.20.0'
$repoRoot = Split-Path -Parent $PSScriptRoot
$targetDir = Join-Path $repoRoot 'source\Assets\Terminal\node'
$targetExe = Join-Path $targetDir 'node.exe'

if (Test-Path $targetExe) {
    Write-Host "node.exe already present at $targetExe" -ForegroundColor Green
    exit 0
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$zipUrl = "https://nodejs.org/dist/v$nodeVersion/node-v$nodeVersion-win-x64.zip"
$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "node-v$nodeVersion-win-x64.zip"
$tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "node-v$nodeVersion-win-x64-extract"

Write-Host "Downloading $zipUrl ..."
Invoke-WebRequest -Uri $zipUrl -OutFile $tempZip

if (Test-Path $tempExtract) {
    Remove-Item -Recurse -Force $tempExtract
}
Expand-Archive -Path $tempZip -DestinationPath $tempExtract

$extractedExe = Join-Path $tempExtract "node-v$nodeVersion-win-x64\node.exe"
Copy-Item -Path $extractedExe -Destination $targetExe -Force

Remove-Item -Force $tempZip
Remove-Item -Recurse -Force $tempExtract

Write-Host "node.exe v$nodeVersion staged at $targetExe" -ForegroundColor Green
