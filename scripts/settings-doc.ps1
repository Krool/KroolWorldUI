<#
.SYNOPSIS
  Regenerates the settings tables in README.md from the plugin's config.Bind calls.
.DESCRIPTION
  build.ps1 runs this before packaging, so the mod page always matches the F7 window. To change
  what a setting says, edit its description in the feature's BindConfig, not the README.
.EXAMPLE
  .\scripts\settings-doc.ps1
#>
param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "tools\SettingsDoc\SettingsDoc.csproj"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

& $dotnet run --project $proj -c Release -v q -p:ValheimDir="$ValheimDir" -- (Join-Path $root "README.md") (Join-Path $ValheimDir "valheim_Data\Managed")
if ($LASTEXITCODE -ne 0) { throw "SettingsDoc failed (exit $LASTEXITCODE)" }
