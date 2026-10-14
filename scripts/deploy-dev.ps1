<#
.SYNOPSIS
  Builds a DEV build of KroolWorldUI and installs it into an r2modman test profile
  (KroolDev by default, never the profile you play on).

.DESCRIPTION
  The dev copy is installed as its own local mod, "Krool-Krool_World_UI_Dev", so r2modman never
  matches it to the Thunderstore package: no update prompts, and an update can never overwrite
  the dev build. If the profile has the online Krool_World_UI installed, it is removed from that
  profile (both would load the same plugin). Your play profile keeps the online version.

  The dev build's settings window title reads "v<version> dev".

.EXAMPLE
  .\scripts\deploy-dev.ps1
  .\scripts\deploy-dev.ps1 -ProfileName KroolDev
#>
param(
    [string]$ProfileName = "KroolDev",
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding $false
$onlineName = "Krool-Krool_World_UI"
$devName = "Krool-Krool_World_UI_Dev"

$profileDir = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim\profiles\$ProfileName"
if (-not (Test-Path $profileDir)) {
    throw "r2modman profile '$ProfileName' not found. Create it with .\scripts\new-dev-profile.ps1"
}
$pluginsDir = Join-Path $profileDir "BepInEx\plugins"
$modsYml = Join-Path $profileDir "mods.yml"

# --- Build (Dev configuration defines DEV_BUILD; output in bin\Dev, separate from release) ---
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
$manifest = Get-Content (Join-Path $root "thunderstore\manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number
& $dotnet build (Join-Path $root "src\KroolWorldUI\KroolWorldUI.csproj") -c Dev -nologo -clp:NoSummary `
    -p:Version=$version -p:ValheimDir="$ValheimDir"
if ($LASTEXITCODE -ne 0) { throw "Dev build failed" }
$dll = Join-Path $root "src\KroolWorldUI\bin\Dev\KroolWorldUI.dll"

# --- Make the profile's mod list say: dev copy present, online copy absent ---
$yml = [IO.File]::ReadAllText($modsYml)
$blocks = [regex]::Split($yml, "(?m)^(?=- manifestVersion)")
$hasOnline = $blocks | Where-Object { $_ -match "(?m)^  name: $([regex]::Escape($onlineName))\s*$" }
$hasDev = $blocks | Where-Object { $_ -match "(?m)^  name: $([regex]::Escape($devName))\s*$" }
$onlineDir = Join-Path $pluginsDir $onlineName

if ($hasOnline -or -not $hasDev -or (Test-Path $onlineDir)) {
    if (Get-Process r2modman -ErrorAction SilentlyContinue) {
        throw "Close r2modman first: this deploy changes the '$ProfileName' mod list, and a running r2modman can write its old list back over it."
    }
    $backup = "$modsYml.bak-kwui"
    if (-not (Test-Path $backup)) { Copy-Item $modsYml $backup }

    $nl = if ($yml.Contains("`r`n")) { "`r`n" } else { "`n" }
    $kept = $blocks | Where-Object { $_ -notmatch "(?m)^  name: $([regex]::Escape($onlineName))\s*$" -and
                                     $_ -notmatch "(?m)^  name: $([regex]::Escape($devName))\s*$" }
    $v = [version]$version
    $devBlock = @(
        "- manifestVersion: 1",
        "  name: $devName",
        "  authorName: Krool",
        "  websiteUrl: https://github.com/Krool/KroolWorldUI",
        "  displayName: Krool_World_UI_Dev",
        "  description: Local dev build of Krool World UI. Not from Thunderstore; redeploy with scripts\deploy-dev.ps1.",
        "  gameVersion: '0'",
        "  networkMode: both",
        "  packageType: other",
        "  installMode: managed",
        "  installedAtTime: $([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())",
        "  loaders: []",
        "  dependencies:",
        "    - denikson-BepInExPack_Valheim-5.4.2351",
        "  incompatibilities: []",
        "  optionalDependencies: []",
        "  versionNumber:",
        "    major: $($v.Major)",
        "    minor: $($v.Minor)",
        "    patch: $($v.Build)",
        "  enabled: true",
        "  onlineSource: false",
        "  trustedPackage: false"
    ) -join $nl
    $body = (($kept -join "").TrimEnd("`r", "`n")) + $nl + $devBlock + $nl
    [IO.File]::WriteAllText($modsYml, $body, $utf8)

    if (Test-Path $onlineDir) {
        Remove-Item $onlineDir -Recurse -Force
        Write-Host "Removed the online $onlineName from '$ProfileName' (your play profile still has it)."
    }
    Write-Host "Registered $devName in '$ProfileName' (backup of the old list: mods.yml.bak-kwui)."
}

# --- Copy the dev build in ---
$target = Join-Path $pluginsDir $devName
New-Item -ItemType Directory -Force $target | Out-Null
try {
    Copy-Item $dll $target -Force
} catch {
    throw "Could not copy the DLL (is Valheim running on the '$ProfileName' profile? Close it first). $_"
}
# Manifest renamed to the dev identity, plus icon/readme so r2modman shows it properly.
$devManifest = (Get-Content (Join-Path $root "thunderstore\manifest.json") -Raw) -replace '"name":\s*"Krool_World_UI"', '"name": "Krool_World_UI_Dev"'
[IO.File]::WriteAllText((Join-Path $target "manifest.json"), $devManifest, $utf8)
Copy-Item (Join-Path $root "thunderstore\icon.png") $target -Force
Copy-Item (Join-Path $root "README.md") $target -Force
Copy-Item (Join-Path $root "CHANGELOG.md") $target -Force
Write-Host "Deployed dev build v$version to $target. Launch Valheim from r2modman with the '$ProfileName' profile; the F7 title reads 'v$version dev'."
