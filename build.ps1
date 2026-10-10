# N.E.K.O-SP2-Bridge build script
#
# Compiles the BepInEx plugin against the game's own managed assemblies.
#
# NOTE: this file is intentionally pure ASCII. Windows PowerShell 5.1 reads
# BOM-less files using the ANSI code page, so non-ASCII text here would break
# parsing on some machines (learned the hard way on the KSP project).
#
# Usage:
#   .\build.ps1                                  # compile -> dist\Sp2Bridge.dll
#   .\build.ps1 -Install                         # compile and copy into the game
#   .\build.ps1 -Debug                           # include the /probe endpoint
#   .\build.ps1 -GameDir "E:\Steam\steamapps\common\SimplePlanes 2"

param(
    [string]$GameDir = "",
    [string]$Csc = "",
    [switch]$Install,
    [switch]$Clean,
    [switch]$Debug
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "src"
$Dist = Join-Path $Root "dist"
$CscTool = Join-Path $Root "tools\roslyn\csc.exe"
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

function Resolve-Compiler {
    param([string]$Requested)
    if ($Requested -and (Test-Path -LiteralPath $Requested -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }
    if (Test-Path -LiteralPath $CscTool -PathType Leaf) {
        return (Resolve-Path -LiteralPath $CscTool).Path
    }
    $onPath = Get-Command "csc.exe" -ErrorAction SilentlyContinue
    if ($null -ne $onPath) { return $onPath.Source }
    $vsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vsWhere -PathType Leaf) {
        $install = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($install) {
            $candidate = Join-Path $install "MSBuild\Current\Bin\Roslyn\csc.exe"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        }
    }
    throw "No C# compiler found. Put Roslyn csc.exe in tools\roslyn\, install VS Build Tools, or pass -Csc."
}

$Game = Find-GameDir -Explicit $GameDir
$Managed = Join-Path $Game "SimplePlanes 2_Data\Managed"
Write-Host "[sp2-bridge] game    : $Game"
Write-Host "[sp2-bridge] managed : $Managed"

if (-not (Test-Path -LiteralPath $Managed -PathType Container)) {
    throw "Managed assembly folder not found: $Managed"
}

if ($Clean) {
    if (Test-Path -LiteralPath $Dist) { Remove-Item -LiteralPath $Dist -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

# --- references ------------------------------------------------------------
$refs = @(
    "mscorlib.dll",
    "netstandard.dll",
    "System.dll",
    "System.Core.dll",
    "System.Runtime.dll",
    "Game.dll",
    "Jundroo.Common.dll",
    "FishNet.Runtime.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.TerrainModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.UIModule.dll",
    "UnityEngine.UI.dll"
)

$refArgs = @()
foreach ($r in $refs) {
    $p = Join-Path $Managed $r
    if (Test-Path -LiteralPath $p -PathType Leaf) { $refArgs += "-r:$p" }
    else { Write-Host "[sp2-bridge] warn: missing reference $r" }
}

# BepInEx provides BaseUnityPlugin / BepInPlugin
$BepCandidates = @(
    (Join-Path $Root "tools\bepinex\BepInEx\core\BepInEx.dll"),
    (Join-Path $Game "BepInEx\core\BepInEx.dll")
)
$BepInExDll = $null
foreach ($c in $BepCandidates) {
    if (Test-Path -LiteralPath $c -PathType Leaf) { $BepInExDll = $c; break }
}
if (-not $BepInExDll) {
    throw "BepInEx.dll not found. Run .\install.ps1 first, or drop BepInEx into tools\bepinex\."
}
$refArgs += "-r:$BepInExDll"
Write-Host "[sp2-bridge] bepinex : $BepInExDll"

# --- compile ---------------------------------------------------------------
$sources = Get-ChildItem -Path $Src -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
if (-not $sources -or $sources.Count -eq 0) { throw "No sources found in $Src" }

$compiler = Resolve-Compiler -Requested $Csc
Write-Host "[sp2-bridge] csc     : $compiler"

$outDll = Join-Path $Dist $DllName
$cscArgs = @(
    "-nologo",
    "-target:library",
    "-langversion:7.3",
    "-optimize+",
    "-debug-",
    "-nostdlib+",
    "-deterministic+",
    "-out:$outDll"
) + $refArgs + $sources

if ($Debug) {
    $cscArgs += "-define:NEKO_DEBUG"
    Write-Host "[sp2-bridge] DEBUG build (the /probe endpoint is included)"
}

Write-Host "[sp2-bridge] compiling $($sources.Count) source files ..."
& $compiler @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

$info = Get-Item -LiteralPath $outDll
Write-Host "[sp2-bridge] built   : $($info.FullName) ($($info.Length) bytes)"

# --- install ---------------------------------------------------------------
$pluginDir = Join-Path $Game "BepInEx\plugins\$PluginName"
if ($Install) {
    if (-not (Test-Path -LiteralPath (Join-Path $Game "BepInEx"))) {
        throw "BepInEx is not installed in $Game. Run .\install.ps1 first."
    }
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    Copy-Item -LiteralPath $outDll -Destination (Join-Path $pluginDir $DllName) -Force
    Write-Host "[sp2-bridge] installed: $pluginDir\$DllName"
}
else {
    Write-Host "[sp2-bridge] plugin dir would be: $pluginDir"
}

Write-Host "[sp2-bridge] done."
