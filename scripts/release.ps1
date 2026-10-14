<#
.SYNOPSIS
  Cuts a release: bumps the version everywhere, builds the zip, optionally publishes it to
  Thunderstore, then commits, tags and pushes.

.DESCRIPTION
  Write the CHANGELOG.md entry ("## <version>") first; the script refuses to release without it,
  because Thunderstore and r2modman show that changelog to players.

  Publishing needs a Thunderstore service-account token for the Krool team, in the
  TCLI_AUTH_TOKEN environment variable (user-level is fine; it is read from the registry so a
  terminal restart is not needed). Set it once in your own terminal, never in a file:
      setx TCLI_AUTH_TOKEN "tss_..."

.EXAMPLE
  .\scripts\release.ps1 -Version 0.2.0            # bump + build + commit/tag, no upload
  .\scripts\release.ps1 -Version 0.2.0 -Publish   # same, and upload to Thunderstore
  .\scripts\release.ps1 -Publish                  # upload the current version (first release)
#>
param(
    [string]$Version,
    [switch]$Publish
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root "thunderstore\manifest.json"
$tomlPath = Join-Path $root "thunderstore.toml"
$pluginPath = Join-Path $root "src\KroolWorldUI\Plugin.cs"
$changelogPath = Join-Path $root "CHANGELOG.md"
$utf8 = New-Object System.Text.UTF8Encoding $false

$current = (Get-Content $manifestPath -Raw | ConvertFrom-Json).version_number
if (-not $Version) { $Version = $current }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
if ([version]$Version -lt [version]$current) { throw "Version $Version is lower than the current $current" }

$changelog = [IO.File]::ReadAllText($changelogPath)
if ($changelog -notmatch "(?m)^## $([regex]::Escape($Version))\s*$") {
    throw "CHANGELOG.md has no '## $Version' section. Write the release notes first."
}

# Token check before changing anything, so a missing token does not leave a half-done release.
$token = $null
if ($Publish) {
    $token = $env:TCLI_AUTH_TOKEN
    if (-not $token) { $token = [Environment]::GetEnvironmentVariable("TCLI_AUTH_TOKEN", "User") }
    if (-not $token) { throw "TCLI_AUTH_TOKEN is not set. Create a service account for the Krool team on thunderstore.io, then run: setx TCLI_AUTH_TOKEN `"<token>`"" }
    $tcli = Join-Path $env:USERPROFILE ".dotnet\tools\tcli.exe"
    if (-not (Test-Path $tcli)) { throw "tcli not found. Install it: dotnet tool install -g tcli" }
}

if ($Version -ne $current) {
    function Set-Version([string]$path, [string]$pattern, [string]$replacement) {
        $text = [IO.File]::ReadAllText($path)
        $new = [regex]::Replace($text, $pattern, $replacement)
        if ($new -eq $text) { throw "Could not find the version in $path" }
        [IO.File]::WriteAllText($path, $new, $utf8)
    }
    Set-Version $manifestPath '"version_number":\s*"[^"]*"' "`"version_number`": `"$Version`""
    Set-Version $tomlPath '(?m)^versionNumber = "[^"]*"' "versionNumber = `"$Version`""
    Set-Version $pluginPath 'Version = "[^"]*"' "Version = `"$Version`""
    Write-Host "Version $current -> $Version"
}

& (Join-Path $PSScriptRoot "build.ps1")
$zip = Join-Path $root "dist\Krool-Krool_World_UI-$Version.zip"
if (-not (Test-Path $zip)) { throw "Expected package not found: $zip" }

if ($Publish) {
    $env:TCLI_AUTH_TOKEN = $token  # process scope only; tcli reads it from here
    Push-Location $root
    try {
        & $tcli publish --file $zip --config-path $tomlPath
        if ($LASTEXITCODE -ne 0) { throw "tcli publish failed (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
        Remove-Item Env:\TCLI_AUTH_TOKEN -ErrorAction SilentlyContinue
    }
    Write-Host "Published Krool-Krool_World_UI $Version. r2modman users will see the update once Thunderstore lists it (a few minutes)."
}

# Commit only the files this script owns (the working tree may be shared with other sessions).
$tag = "v$Version"
git -C $root add -- thunderstore/manifest.json thunderstore.toml src/KroolWorldUI/Plugin.cs CHANGELOG.md
git -C $root diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git -C $root commit -q -m "Release $Version"
}
if (-not (git -C $root tag --list $tag)) {
    git -C $root tag -a $tag -m "Release $Version"  # annotated: --follow-tags only pushes these
}
git -C $root push -q --follow-tags
Write-Host "Tagged $tag and pushed."
