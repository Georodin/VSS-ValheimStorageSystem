param(
    [string]$Configuration = "Release",
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"

if (!$NoInstall -and (Get-Process -Name valheim -ErrorAction SilentlyContinue)) {
    throw 'Close Valheim before installing VSS. Use -NoInstall to build safely while the game is open, or test-isolated.ps1 for separate headless tests.'
}

if (-not (Test-Path (Join-Path $PSScriptRoot "..\BepInEx\core\BepInEx.dll"))) {
    throw "BepInEx was not found in the Valheim folder. Install BepInExPack_Valheim first, then run this build again."
}

$sdk = Get-ChildItem "C:\Program Files\dotnet\sdk" -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
$compiler = Join-Path $sdk.FullName "Roslyn\bincore\csc.dll"
if (-not (Test-Path $compiler)) {
    throw "Could not find the Roslyn C# compiler under C:\Program Files\dotnet\sdk."
}

$binDir = Join-Path $PSScriptRoot "bin"
$objDir = Join-Path $PSScriptRoot "obj"
New-Item -ItemType Directory -Force -Path $binDir, $objDir | Out-Null

$managed = Join-Path $PSScriptRoot "..\valheim_Data\Managed"
$bepInEx = Join-Path $PSScriptRoot "..\BepInEx\core"
$references = @(
    "mscorlib.dll",
    "System.dll",
    "System.Core.dll",
    "netstandard.dll",
    "assembly_valheim.dll",
    "assembly_guiutils.dll",
    "assembly_utils.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.AssetBundleModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.ImageConversionModule.dll",
    "UnityEngine.InputLegacyModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.TextRenderingModule.dll",
    "UnityEngine.UI.dll",
    "Unity.TextMeshPro.dll",
    "UnityEngine.TextCoreFontEngineModule.dll",
    "UnityEngine.UIModule.dll",
    "UnityEngine.AnimationModule.dll",
    "SoftReferenceableAssets.dll"
) | ForEach-Object { Join-Path $managed $_ }

$references += @(
    (Join-Path $bepInEx "BepInEx.dll"),
    (Join-Path $bepInEx "0Harmony.dll")
)

$rsp = Join-Path $objDir "compile.rsp"
$sources = Get-ChildItem (Join-Path $PSScriptRoot "src") -Filter *.cs -File | Sort-Object Name
@(
    "/target:library",
    "/nologo",
    "/nostdlib+",
    "/langversion:latest",
    "/out:`"$(Join-Path $binDir "VSS.dll")`""
) + ($references | ForEach-Object { "/reference:`"$($_)`"" }) + ($sources | ForEach-Object { "`"$($_.FullName)`"" }) |
    Set-Content -Path $rsp -Encoding UTF8

dotnet $compiler "@$rsp"
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed."
}

if ($NoInstall) {
    Write-Host "VSS compiled to $(Join-Path $binDir 'VSS.dll')"
    return
}

$pluginDir = Join-Path $PSScriptRoot "..\BepInEx\plugins\VSS"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item (Join-Path $PSScriptRoot "bin\VSS.dll") $pluginDir -Force
$uiDir = Join-Path $pluginDir 'ui'
New-Item -ItemType Directory -Force -Path $uiDir | Out-Null
foreach ($asset in @('outer-frame.png', 'backing.png', 'panel-skin.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\ui\$asset") -Destination $uiDir -Force
}
$iconsDir = Join-Path $pluginDir 'icons'
New-Item -ItemType Directory -Force -Path $iconsDir | Out-Null
foreach ($asset in @('controller.png', 'chest-preference.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\icons\$asset") -Destination $iconsDir -Force
}
$legacyPanel = Join-Path $pluginDir 'vss-panel.png'
if (Test-Path -LiteralPath $legacyPanel) { Remove-Item -LiteralPath $legacyPanel -Force }

$bundle = Join-Path $PSScriptRoot "assets\vss_assets"
if (Test-Path $bundle) {
    Copy-Item $bundle $pluginDir -Force
}

Write-Host "VSS installed to $pluginDir"
