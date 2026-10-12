param([int]$TimeoutSeconds = 180)
$ErrorActionPreference = 'Stop'
# Unlike test.ps1, this never installs into or launches from the live game directory.
& (Join-Path $PSScriptRoot 'build.ps1') -NoInstall
$gameDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'tests/runtime/isolated-game'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
foreach ($file in @('valheim.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'winhttp.dll', 'doorstop_config.ini', 'steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $gameDir $file) -Destination (Join-Path $testRoot $file) -Force
}
# Game assets/runtime are only read. Config, BepInEx files, logs and saves are separate.
foreach ($directory in @('valheim_Data', 'MonoBleedingEdge')) {
    $link = Join-Path $testRoot $directory
    $target = Join-Path $gameDir $directory
    if (!(Test-Path -LiteralPath $link)) { New-Item -ItemType Junction -Path $link -Target $target | Out-Null }
    $existing = Get-Item -LiteralPath $link
    if (!$existing.LinkType -or [IO.Path]::GetFullPath($existing.Target) -ne [IO.Path]::GetFullPath($target)) {
        throw "Unexpected test runtime link: $link"
    }
}
$core = Join-Path $testRoot 'BepInEx/core'
New-Item -ItemType Directory -Force -Path $core | Out-Null
Copy-Item -Path (Join-Path $gameDir 'BepInEx/core/*') -Destination $core -Recurse -Force
$plugin = Join-Path $testRoot 'BepInEx/plugins/VSS'
New-Item -ItemType Directory -Force -Path $plugin | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/VSS.dll') -Destination $plugin -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/vss_assets') -Destination $plugin -Force
foreach ($folder in @('ui', 'icons')) {
    $destination = Join-Path $plugin $folder
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $files = if ($folder -eq 'ui') { @('backing.png','outer-frame.png','panel-skin.png') } else { @('controller.png','chest-preference.png') }
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets/$folder/$file") -Destination $destination -Force }
}
$report = Join-Path $testRoot 'report.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
$arguments = @('-batchmode', '-nographics', '-job-worker-count', '1', '-vss-self-test', '-vss-test-report', ('"'+$report+'"'),
    '-savedir', ('"'+(Join-Path $testRoot 'saves')+'"'), '-logFile', ('"'+(Join-Path $testRoot 'unity.log')+'"'))
$testProcess = Start-Process -FilePath (Join-Path $testRoot 'valheim.exe') -ArgumentList $arguments -WorkingDirectory $testRoot -WindowStyle Hidden -PassThru
try { $testProcess.PriorityClass = 'Idle' } catch { Write-Warning 'Could not lower the isolated test priority.' }
Write-Output "Isolated test PID: $($testProcess.Id). Live game installation is untouched."
if (!$testProcess.WaitForExit($TimeoutSeconds * 1000)) {
    $testProcess.Kill()
    throw "Only the isolated test process was stopped after timeout. Inspect $testRoot/unity.log"
}
$result = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { 'No report produced.' }
if ($result -notmatch '^PASS:' -or $testProcess.ExitCode -ne 0) { throw "Isolated tests failed (exit $($testProcess.ExitCode)): $result" }
Write-Output $result
$performance = [IO.Path]::ChangeExtension($report, 'performance.txt')
if (Test-Path -LiteralPath $performance) { Get-Content -LiteralPath $performance }
