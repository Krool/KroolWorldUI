<#
.SYNOPSIS
  Copies an existing r2modman profile (mods + configs) into a test profile, so the mod can be
  tested next to your real mod list without touching the profile you play on.
  Characters and worlds are NOT in r2modman profiles; they are shared by all profiles.
.EXAMPLE
  .\scripts\new-dev-profile.ps1 -From BCCBoys -To KroolDev
#>
param(
    [string]$From = "BCCBoys",
    [string]$To = "KroolDev"
)
$ErrorActionPreference = "Stop"
$profiles = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim\profiles"
$src = Join-Path $profiles $From
$dst = Join-Path $profiles $To
if (-not (Test-Path $src)) { throw "Profile '$From' not found in $profiles" }
if (Test-Path $dst) { throw "Profile '$To' already exists. Delete it in r2modman first if you want a fresh copy." }
Copy-Item $src $dst -Recurse
Write-Host "Created r2modman profile '$To' from '$From'. Restart r2modman to see it."
