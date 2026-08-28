#Requires -Version 5.1
<#
.SYNOPSIS
    Builds Claudium and mirrors the output to publish-fresh\ — the folder the
    installed Start Menu shortcut actually launches (see the .lnk in
    %AppData%\Microsoft\Windows\Start Menu\Programs\Claudium.lnk).

.DESCRIPTION
    `dotnet build` alone only updates source\bin\... — it never touches
    publish-fresh\, so the running app can silently stay on an old build
    while source keeps changing.

    This deliberately does NOT use `dotnet publish`: for this project it
    silently skips regenerating the compiled XAML (App.xbf/MainPage.xbf/
    MainWindow.xbf) and the resource index (Claudium.pri), so publish-fresh
    ends up with a fresh Claudium.dll paired with stale XAML binaries —
    Microsoft.UI.Xaml.dll crashes on startup (0xc000027b) the moment the
    two disagree about what a page contains. `dotnet build`'s bin\...
    output reliably regenerates all of it, so that's mirrored instead via
    robocopy /MIR, which also deletes anything in publish-fresh that no
    longer exists in the build output (no stale-file accumulation).

    Check the version + build-timestamp line at the bottom of the launcher
    page after publishing to confirm the new build is what's running.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'source'
$buildOutputDir = Join-Path $sourceDir 'bin\x64\Release\net8.0-windows10.0.26100.0\win-x64'
$publishDir = Join-Path $repoRoot 'publish-fresh'

& (Join-Path $repoRoot 'scripts\fetch-node.ps1')

Push-Location $sourceDir
try {
    dotnet build -c Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

foreach ($required in @('App.xbf', 'MainPage.xbf', 'MainWindow.xbf', 'Claudium.pri')) {
    if (-not (Test-Path (Join-Path $buildOutputDir $required))) {
        throw "Build output is missing $required — refusing to publish a build that would crash on startup."
    }
}

# /MIR mirrors source into destination, deleting anything in publish-fresh that no
# longer exists in the build output — this is what keeps stale files from ever
# accumulating there again. If Claudium.exe is currently running, its locked files
# will fail to copy; close the app first and re-run.
robocopy $buildOutputDir $publishDir /MIR /R:2 /W:1 /NFL /NDL /NJH
$robocopyExitCode = $LASTEXITCODE
# Robocopy exit codes 0-7 are all success (bitflags for copied/skipped/mismatched files);
# 8+ indicates a real failure.
if ($robocopyExitCode -ge 8) {
    throw "robocopy failed with exit code $robocopyExitCode"
}

Write-Host "Published to $publishDir" -ForegroundColor Green
