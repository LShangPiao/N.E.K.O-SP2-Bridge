# N.E.K.O-SP2-Bridge release packaging
#
# Produces a zip that users can drop straight into the game folder:
#   dist\N.E.K.O-SP2-Bridge-v<version>-win-x64.zip
#
# NOTE: pure ASCII on purpose - Windows PowerShell 5.1 reads BOM-less files
# with the ANSI code page.

param(
    [string]$Version = "0.1.0",
    [string]$GameDir = "",
    [switch]$Debug
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$PluginName = "N.E.K.O-SP2-Bridge"
$DllName = "Sp2Bridge.dll"

& (Join-Path $Root "build.ps1") -GameDir $GameDir -Debug:$Debug
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$stage = Join-Path $Root "dist\stage"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$pluginDir = Join-Path $stage "BepInEx\plugins\$PluginName"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -LiteralPath (Join-Path $Root "dist\$DllName") -Destination (Join-Path $pluginDir $DllName) -Force

# README inside the zip: users unpacking into the game folder should see it.
Copy-Item -LiteralPath (Join-Path $Root "README.md") -Destination (Join-Path $pluginDir "README.md") -Force
if (Test-Path -LiteralPath (Join-Path $Root "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $Root "LICENSE") -Destination (Join-Path $pluginDir "LICENSE") -Force
}

$zipName = "$PluginName-v$Version-win-x64.zip"
$zipPath = Join-Path $Root "dist\$zipName"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)

Write-Host "[package] $zipPath"

$sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$sums = Join-Path $Root "dist\SHA256SUMS.txt"
Set-Content -LiteralPath $sums -Value ("$sha  $zipName") -Encoding ASCII
Write-Host "[package] SHA256: $sha"
