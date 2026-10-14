<#
.SYNOPSIS
  Builds and copies KroolWorldUI.dll into an r2modman profile for testing.
  Defaults to the KroolDev test profile, never the profile you play on.
.EXAMPLE
  .\scripts\deploy-dev.ps1
  .\scripts\deploy-dev.ps1 -ProfileName KroolDev -NoBuild
#>
param(
    [string]$ProfileName = "KroolDev",
    [switch]$NoBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not $NoBuild) { & (Join-Path $PSScriptRoot "build.ps1") }

$profileDir = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim\profiles\$ProfileName"
if (-not (Test-Path $profileDir)) {
    throw "r2modman profile '$ProfileName' not found. Create it with .\scripts\new-dev-profile.ps1"
}
$target = Join-Path $profileDir "BepInEx\plugins\Krool-Krool_World_UI"
New-Item -ItemType Directory -Force $target | Out-Null

$dll = Join-Path $root "src\KroolWorldUI\bin\Release\KroolWorldUI.dll"
try {
    Copy-Item $dll $target -Force
} catch {
    throw "Could not copy the DLL (is Valheim running on the '$ProfileName' profile? Close it first). $_"
}
Copy-Item (Join-Path $root "thunderstore\manifest.json") $target -Force
Write-Host "Deployed to $target. Launch Valheim from r2modman with the '$ProfileName' profile."
