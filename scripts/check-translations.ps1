# Checks every Translations\<Language>.txt and .settings.txt against the English files: missing
# keys (they fall back to English), keys English no longer has, and $1/$2 placeholders that were
# dropped or added. Prints a short report; exits 1 only with -Strict and problems found.
# Run by build.ps1 after the settings source is regenerated.
param([switch]$Strict)

$dir = Join-Path $PSScriptRoot "..\src\KroolWorldUI\Translations"

function Read-Table([string]$path) {
    $table = [ordered]@{}
    if (-not (Test-Path $path)) { return $table }
    foreach ($line in [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith("#")) { continue }
        $eq = $t.IndexOf("=")
        if ($eq -le 0) { continue }
        $table[$t.Substring(0, $eq).Trim()] = $t.Substring($eq + 1).Trim()
    }
    return $table
}

function Placeholders([string]$text) {
    return (([regex]::Matches($text, '\$\d')) | ForEach-Object { $_.Value } | Sort-Object -Unique) -join ","
}

$sources = @{
    ".txt"          = Read-Table (Join-Path $dir "English.txt")
    ".settings.txt" = Read-Table (Join-Path $dir "English.settings.txt")
}
$languages = Get-ChildItem $dir -Filter *.txt |
    Where-Object { $_.Name -notlike "English*" } |
    ForEach-Object { $_.Name -replace '\.settings\.txt$', '' -replace '\.txt$', '' } |
    Sort-Object -Unique

$problems = 0
foreach ($lang in $languages) {
    $notes = @()
    foreach ($ext in $sources.Keys) {
        $english = $sources[$ext]
        $local = Read-Table (Join-Path $dir "$lang$ext")
        $missing = @($english.Keys | Where-Object { -not $local.Contains($_) })
        $extra = @($local.Keys | Where-Object { -not $english.Contains($_) })
        $badArgs = @($english.Keys | Where-Object { $local.Contains($_) -and (Placeholders $english[$_]) -ne (Placeholders $local[$_]) })
        if ($missing.Count) { $notes += "$($missing.Count) missing in $ext (English shows): $(($missing | Select-Object -First 5) -join ', ')" }
        if ($extra.Count) { $notes += "$($extra.Count) unknown keys in ${ext}: $(($extra | Select-Object -First 5) -join ', ')" }
        if ($badArgs.Count) { $notes += "placeholders differ in ${ext}: $(($badArgs | Select-Object -First 5) -join ', ')" }
    }
    if ($notes.Count) {
        $problems += $notes.Count
        Write-Host "  ${lang}:" -ForegroundColor Yellow
        $notes | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
    }
}
if ($problems -eq 0) { Write-Host "Translations: $($languages.Count) languages, all complete." }
elseif ($Strict) { exit 1 }
