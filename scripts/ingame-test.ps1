<#
.SYNOPSIS
  Runs the scripted in-game test: starts Valheim on the KroolDev profile with tools\InGameTest
  installed for this one launch, lets it play (boss kill, player death, log out, log in), and
  prints what the mod logged. Nobody needs to touch the game; it quits by itself.

.DESCRIPTION
  Tests the dev build already in the profile, so run scripts\deploy-dev.ps1 first.
  The game opens in a small window for about three minutes and takes the GPU while it runs.
  It uses a throwaway local character and world named "kwuitest", deleted afterwards unless
  -KeepSaves (a kept world loads faster on the next run). Needs Steam running and Valheim closed.

.EXAMPLE
  .\scripts\deploy-dev.ps1; .\scripts\ingame-test.ps1
#>
param(
    [string]$ProfileName = "KroolDev",
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [int]$TimeoutSeconds = 660,
    [switch]$KeepSaves
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (Get-Process valheim -ErrorAction SilentlyContinue) { throw "Valheim is already running. Close it first." }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw "Steam is not running; the game cannot start without it." }

$profileDir = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim\profiles\$ProfileName"
$pluginsDir = Join-Path $profileDir "BepInEx\plugins"
$preloader = Join-Path $profileDir "BepInEx\core\BepInEx.Preloader.dll"
$log = Join-Path $profileDir "BepInEx\LogOutput.log"
if (-not (Test-Path (Join-Path $pluginsDir "Krool-Krool_World_UI_Dev\KroolWorldUI.dll"))) {
    throw "No dev build in '$ProfileName'. Run .\scripts\deploy-dev.ps1 first."
}
if (-not (Test-Path $preloader)) { throw "BepInEx not found in profile '$ProfileName'." }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
& $dotnet build (Join-Path $root "tools\InGameTest\InGameTest.csproj") -c Release -nologo -clp:NoSummary -p:ValheimDir="$ValheimDir" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Test plugin build failed" }

# Only files this test makes: the "kwuitest" character, world and saved recap history.
$saves = Join-Path $env:USERPROFILE "AppData\LocalLow\IronGate\Valheim"
function Remove-TestFiles([bool]$savesToo) {
    $targets = @(Join-Path $profileDir "BepInEx\config\KroolWorldUI\recaps_kwuitest_*")
    if ($savesToo) {
        $targets += Join-Path $saves "worlds_local\kwuitest*"
        $targets += Join-Path $saves "characters_local\kwuitest*"
    }
    foreach ($t in $targets) {
        Get-ChildItem $t -File -ErrorAction SilentlyContinue | Remove-Item -Force -Confirm:$false
    }
    # The game keeps a world as a folder of that name (chunks, map cache), not only loose files.
    $worldDir = Join-Path $saves "worlds_local\kwuitest"
    if ($savesToo -and (Test-Path $worldDir -PathType Container)) {
        Remove-Item $worldDir -Recurse -Force -Confirm:$false
    }
}
Remove-TestFiles $false   # a recap file from an earlier run would change what this one expects

$testDir = Join-Path $pluginsDir "KwuiInGameTest"
New-Item -ItemType Directory -Force $testDir | Out-Null
Copy-Item (Join-Path $root "tools\InGameTest\bin\Release\KwuiInGameTest.dll") $testDir -Force

$started = Get-Date
$process = $null
try {
    # What r2modman passes, minus Steam's launcher: steam_appid.txt in the game folder lets the exe start directly.
    $gameArgs = "--doorstop-enabled true --doorstop-target-assembly `"$preloader`" -screen-fullscreen 0 -screen-width 960 -screen-height 540"
    $process = Start-Process (Join-Path $ValheimDir "valheim.exe") -ArgumentList $gameArgs -WorkingDirectory $ValheimDir -PassThru
    try { $process.PriorityClass = "BelowNormal" } catch { }
    Write-Host "Valheim started (pid $($process.Id)); waiting up to $TimeoutSeconds s for the test to finish..."
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Write-Host "Still running after $TimeoutSeconds s; closing it." -ForegroundColor Yellow
        Stop-Process -Id $process.Id -Force -Confirm:$false
    }
}
finally {
    Remove-Item $testDir -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
    Remove-TestFiles (-not $KeepSaves)
}

if (-not (Test-Path $log) -or (Get-Item $log).LastWriteTime -lt $started) {
    throw "The game wrote no BepInEx log: it did not start modded."
}
$lines = [IO.File]::ReadAllLines($log)
Write-Host "`n--- test and mod lines from $log"
$lines | Where-Object { $_ -match '\[KWUITEST\]|Krool World UI\]|Boss recap|Recap history|Death recap' } | ForEach-Object { Write-Host $_ }
$errors = @($lines | Where-Object { $_ -match '^\[(Error|Fatal)' })
Write-Host "`n--- $($errors.Count) error lines in the whole log (all mods); those naming this mod:"
$mine = @($lines | Select-String -Pattern 'KroolWorldUI|Krool World UI|KwuiInGameTest' | Where-Object { $_.Line -match 'Error|Exception|  at ' })
$mine | Select-Object -First 40 | ForEach-Object { Write-Host $_.Line }
if (-not ($lines -match '\[KWUITEST\] DONE')) { Write-Host "`nTest did NOT run to the end." -ForegroundColor Yellow; exit 1 }
if ($lines -match '\[KWUITEST\] RESULT: PASS') { Write-Host "`nPASS" } else { Write-Host "`nFAIL: see the FAIL lines above." -ForegroundColor Yellow; exit 1 }
