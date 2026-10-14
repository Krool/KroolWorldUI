<#
.SYNOPSIS
  Builds KroolWorldUI and packages a Thunderstore/r2modman zip into dist\.
.EXAMPLE
  .\scripts\build.ps1
  .\scripts\build.ps1 -ValheimDir "D:\SteamLibrary\steamapps\common\Valheim"
#>
param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\KroolWorldUI\KroolWorldUI.csproj"
$manifestPath = Join-Path $root "thunderstore\manifest.json"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw ".NET SDK not found. Install it: winget install Microsoft.DotNet.SDK.8" }

# One version, three places: manifest.json, Plugin.cs, csproj. Refuse to package if they drift.
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.version_number
$pluginSrc = Get-Content (Join-Path $root "src\KroolWorldUI\Plugin.cs") -Raw
if ($pluginSrc -notmatch "Version = `"$([regex]::Escape($version))`"") {
    throw "Plugin.cs Version does not match manifest.json version_number $version"
}

& $dotnet build $proj -c $Configuration -nologo -clp:NoSummary -p:Version=$version -p:ValheimDir="$ValheimDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$dll = Join-Path $root "src\KroolWorldUI\bin\$Configuration\KroolWorldUI.dll"
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "package"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item $dll $stage
Copy-Item $manifestPath $stage
Copy-Item (Join-Path $root "thunderstore\icon.png") $stage
# The package README is the Thunderstore mod page: players only. Everything from
# "## Building from source" on is for developers and stays on GitHub.
$readme = [IO.File]::ReadAllText((Join-Path $root "README.md"))
$cut = $readme.IndexOf("## Building from source")
if ($cut -gt 0) { $readme = $readme.Substring(0, $cut).TrimEnd() + "`n" }
[IO.File]::WriteAllText((Join-Path $stage "README.md"), $readme, (New-Object System.Text.UTF8Encoding $false))
Copy-Item (Join-Path $root "CHANGELOG.md") $stage

$zip = Join-Path $dist "Krool-$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Packaged $zip"
