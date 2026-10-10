# N.E.K.O-SP2-Bridge installer
#
# Installs BepInEx (the mod loader) plus this plugin into SimplePlanes 2.
#
# It does NOT touch any original game file. Everything it adds lives in
#   <game>\winhttp.dll            (BepInEx loader proxy)
#   <game>\doorstop_config.ini
#   <game>\.doorstop_version
#   <game>\BepInEx\...            (loader + this plugin)
# To uninstall, delete exactly those. See README.md.
#
# BepInEx 5 Mono (x64) is required - SP2 is a Unity Mono game, NOT IL2CPP.
# Download: https://github.com/BepInEx/BepInEx/releases  (BepInEx_win_x64_5.x.x.x.zip)
#
# NOTE: pure ASCII on purpose - Windows PowerShell 5.1 reads BOM-less files
# with the ANSI code page.

param(
    [string]$GameDir = "",
    [string]$BepInExZip = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$PluginName = "N.E.K.O-SP2-Bridge"
$DllName = "Sp2Bridge.dll"

function Find-GameDir {
    param([string]$Explicit)
    if ($Explicit) {
        if (Test-Path -LiteralPath (Join-Path $Explicit "SimplePlanes 2.exe")) { return $Explicit }
        throw "GameDir does not contain 'SimplePlanes 2.exe': $Explicit"
    }
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\SimplePlanes 2",
        "C:\Program Files\Steam\steamapps\common\SimplePlanes 2",
        "D:\Steam\steamapps\common\SimplePlanes 2",
        "D:\SteamLibrary\steamapps\common\SimplePlanes 2",
        "E:\Steam\steamapps\common\SimplePlanes 2",
        "E:\SteamLibrary\steamapps\common\SimplePlanes 2",
        "E:\Game\steam\steamapps\common\SimplePlanes 2",
        "F:\SteamLibrary\steamapps\common\SimplePlanes 2"
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $c "SimplePlanes 2.exe")) { return $c }
    }
    throw "Could not locate SimplePlanes 2. Pass -GameDir explicitly."
}

$Game = Find-GameDir -Explicit $GameDir
Write-Host "[install] game: $Game"

# --- 1. BepInEx ------------------------------------------------------------
$bepRoot = Join-Path $Game "BepInEx"
if (-not (Test-Path -LiteralPath (Join-Path $bepRoot "core\BepInEx.dll"))) {
    if (-not $BepInExZip) {
        $BepInExZip = Join-Path $Root "tools\dl\BepInEx_win_x64_5.4.23.5.zip"
    }
    if (-not (Test-Path -LiteralPath $BepInExZip -PathType Leaf)) {
        throw @"
BepInEx is not installed and no zip was found.
Download BepInEx 5 (Mono, Windows x64) from
  https://github.com/BepInEx/BepInEx/releases
then either
  - put the zip at: $BepInExZip
  - or pass: .\install.ps1 -GameDir "$Game" -BepInExZip "<path to zip>"
"@
    }
    Write-Host "[install] extracting BepInEx from $BepInExZip"
    $tmp = Join-Path $env:TEMP ("neko_sp2_bepinex_" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($BepInExZip, $tmp)
        $inner = Get-ChildItem -LiteralPath $tmp -Directory | Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName "BepInEx")
        } | Select-Object -First 1
        $source = if ($inner) { $inner.FullName } else { $tmp }
        Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
            $dest = Join-Path $Game $_.Name
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "[install] BepInEx installed"
}
else {
    Write-Host "[install] BepInEx already present, skipping"
}

# --- 2. this plugin --------------------------------------------------------
$dll = Join-Path $Root "dist\$DllName"
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Plugin DLL not found at $dll. Run .\build.ps1 first."
}
$pluginDir = Join-Path $bepRoot "plugins\$PluginName"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDir $DllName) -Force
Write-Host "[install] plugin installed: $pluginDir\$DllName"

Write-Host ""
Write-Host "[install] done. Start SimplePlanes 2 and enter a flight, then check:"
Write-Host "          http://127.0.0.1:21580/ping"
