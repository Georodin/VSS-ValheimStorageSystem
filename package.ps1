param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -NoInstall
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
} else {
    $existingDll = Get-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/VSS.dll')
    if (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter *.cs -File | Where-Object LastWriteTimeUtc -gt $existingDll.LastWriteTimeUtc) {
        throw 'Source changed since the existing build. Rebuild and test before packaging with -SkipBuild.'
    }
}

$manifestPath = Join-Path $PSScriptRoot "manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$packageName = "$($manifest.name)-$($manifest.version_number)"
$distDir = Join-Path $PSScriptRoot "dist"
$stageDir = Join-Path $distDir $packageName
$zipPath = Join-Path $distDir "$packageName.zip"

$distRoot = [IO.Path]::GetFullPath($distDir).TrimEnd('\') + '\'
$stageDir = [IO.Path]::GetFullPath($stageDir)
$zipPath = [IO.Path]::GetFullPath($zipPath)
if (-not $stageDir.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $zipPath.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package paths must stay within the project dist directory.'
}

if (Test-Path $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
Copy-Item $manifestPath (Join-Path $stageDir "manifest.json") -Force
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $stageDir "README.md") -Force
Copy-Item (Join-Path $PSScriptRoot "CHANGELOG.md") (Join-Path $stageDir "CHANGELOG.md") -Force
$pluginDir = Join-Path $stageDir 'plugins\VSS'
New-Item -ItemType Directory -Force -Path (Join-Path $pluginDir 'ui') | Out-Null
Copy-Item (Join-Path $PSScriptRoot "bin\VSS.dll") (Join-Path $pluginDir "VSS.dll") -Force
foreach ($asset in @('outer-frame.png', 'backing.png', 'panel-skin.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\ui\$asset") -Destination (Join-Path $pluginDir 'ui') -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $pluginDir 'icons') | Out-Null
foreach ($asset in @('controller.png', 'chest-preference.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\icons\$asset") -Destination (Join-Path $pluginDir 'icons') -Force
}

$bundle = Join-Path $PSScriptRoot "assets\vss_assets"
if (Test-Path $bundle) {
    Copy-Item $bundle (Join-Path $pluginDir "vss_assets") -Force
}

$iconPath = Join-Path $stageDir "icon.png"
Add-Type -AssemblyName System.Drawing
$sourceIcon = Join-Path $PSScriptRoot 'icon.png'
$image = [System.Drawing.Image]::FromFile($sourceIcon)
try {
    if ($image.Width -ne 256 -or $image.Height -ne 256 -or
        $image.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw 'Thunderstore icon.png must be a 256x256 PNG.'
    }
} finally { $image.Dispose() }
Copy-Item -LiteralPath $sourceIcon -Destination $iconPath -Force
if ($manifest.name -notmatch '^[a-zA-Z0-9_]{1,128}$' -or
    $manifest.version_number -notmatch '^\d+\.\d+\.\d+$' -or
    $manifest.description.Length -gt 250) {
    throw 'Invalid Thunderstore manifest name, version or description.'
}

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -Force
Write-Host "Package ready: $zipPath"
