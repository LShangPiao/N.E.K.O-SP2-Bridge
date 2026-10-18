# Package N.E.K.O-SP2-Bridge into a drop-in zip.
#
# The zip is meant to be extracted straight into the game folder, so it
# carries BepInEx itself as well as our plugin:
#
#   <game>/
#   +-- winhttp.dll                    <- BepInEx loader
#   +-- doorstop_config.ini            <- BepInEx loader
#   +-- .doorstop_version              <- BepInEx loader
#   +-- BepInEx/
#       +-- core/                      <- BepInEx runtime
#       +-- plugins/N.E.K.O-SP2-Bridge/
#           +-- Sp2Bridge.dll          <- our plugin
#
# Users who already have BepInEx installed only need the plugins/ subtree;
# the README inside the zip says so.
#
# NOTE: keep this file pure ASCII - Windows PowerShell 5.1 reads BOM-less
# files with the ANSI code page, and non-ASCII bytes break parsing.

param(
    [string]$Version = "0.1.0",
    [string]$GameDir = "",
    [string]$OutDir = "",
    [switch]$Debug,
    # Skip build.ps1 and package whatever is already in dist\.
    # CI uses this: compiling needs the game's own assemblies, which cannot
    # be shipped to a runner, so the DLL is committed to the repo instead.
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$PluginName = "N.E.K.O-SP2-Bridge"
$DllName = "Sp2Bridge.dll"

# --- 1) build ---------------------------------------------------------------
if ($SkipBuild) {
    Write-Host "[package] -SkipBuild: using the existing DLL in dist\"
} else {
    & (Join-Path $Root "build.ps1") -GameDir $GameDir -Debug:$Debug
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}

$Dll = Join-Path $Root "dist\$DllName"
if (-not (Test-Path -LiteralPath $Dll)) { throw "missing build output: $Dll" }

# --- 2) locate the BepInEx payload -----------------------------------------
$Payload = Join-Path $Root "tools\bepinex-payload"
if (-not (Test-Path -LiteralPath (Join-Path $Payload "winhttp.dll"))) {
    throw ("BepInEx payload missing at $Payload.`n" +
           "See tools\README.md - drop BepInEx 5.4.23.5 x64 there, or copy it " +
           "from an existing install (BepInEx has no game-specific bits).")
}

if (-not $OutDir) { $OutDir = Join-Path $Root "dist" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$stage = Join-Path $Root "dist\stage"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# --- 3) stage BepInEx loader at the game root -------------------------------
Write-Host "[package] staging BepInEx loader ..."
foreach ($f in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")) {
    $src = Join-Path $Payload $f
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $stage $f) -Force
    }
}
Copy-Item -LiteralPath (Join-Path $Payload "BepInEx") `
          -Destination (Join-Path $stage "BepInEx") -Recurse -Force

# --- 4) stage our plugin ----------------------------------------------------
$pluginDir = Join-Path $stage "BepInEx\plugins\$PluginName"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -LiteralPath $Dll -Destination (Join-Path $pluginDir $DllName) -Force
Copy-Item -LiteralPath (Join-Path $Root "README.md") -Destination (Join-Path $pluginDir "README.md") -Force
if (Test-Path -LiteralPath (Join-Path $Root "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $Root "LICENSE") -Destination (Join-Path $pluginDir "LICENSE") -Force
}

# --- 5) plain-text install notes at the zip root ----------------------------
$readme = @"
N.E.K.O-SP2-Bridge v$Version  (Neko Uplink)
Read-only flight telemetry for SimplePlanes 2, over a local HTTP API.
https://github.com/LShangPiao/N.E.K.O-SP2-Bridge

HOW TO INSTALL
--------------
Extract everything in this zip into the game folder, next to
"SimplePlanes 2.exe". Typical Steam path:
  F:\SteamLibrary\steamapps\common\SimplePlanes 2

After extracting, the folder should contain:
  SimplePlanes 2.exe
  winhttp.dll               <- added
  doorstop_config.ini       <- added
  .doorstop_version         <- added
  BepInEx\                  <- added
  SimplePlanes 2_Data\

Start the game, then open:
  http://127.0.0.1:21580/ping

ALREADY HAVE BEPINEX?
---------------------
Then you only need:
  BepInEx\plugins\N.E.K.O-SP2-Bridge\

LOGS
----
  BepInEx\plugins\N.E.K.O-SP2-Bridge\sp2-bridge.log
  BepInEx\LogOutput.log

CONFIG
------
BepInEx\plugins\N.E.K.O-SP2-Bridge\neko-sp2-bridge.ini
  port=21580            HTTP port (localhost only)
  poll_interval_ms=200  telemetry sampling interval, 50-60000
  log_to_file=true

UNINSTALL
---------
Delete winhttp.dll, doorstop_config.ini, .doorstop_version and the BepInEx
folder. No original game file is ever modified.

READ-ONLY
---------
This mod only serves GET requests and never changes game state.
"@
[System.IO.File]::WriteAllText((Join-Path $stage "README.txt"), $readme,
    (New-Object System.Text.UTF8Encoding($false)))

# --- 6) zip -----------------------------------------------------------------
$zipName = "$PluginName-v$Version-win-x64.zip"
$zipPath = Join-Path $OutDir $zipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)

Write-Host "[package] $zipPath  ($((Get-Item -LiteralPath $zipPath).Length) bytes)"

# --- 7) checksum ------------------------------------------------------------
$sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$sums = Join-Path $OutDir "SHA256SUMS.txt"
Set-Content -LiteralPath $sums -Value ("$sha  $zipName") -Encoding ASCII
Write-Host "[package] SHA256: $sha"

# --- 8) show what went in ---------------------------------------------------
Write-Host "[package] contents:"
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($e in $archive.Entries) {
        Write-Host ("   {0}  ({1})" -f $e.FullName, $e.Length)
    }
} finally { $archive.Dispose() }

Remove-Item -LiteralPath $stage -Recurse -Force
Write-Host "[package] done."
