<#
.SYNOPSIS
  Regenerates thunderstore\icon.png (256x256, required by Thunderstore).
  Motif: the two-shade XP bar under a "KW" mark, in Valheim's dark wood and gold.
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "thunderstore\icon.png"

$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = "AntiAlias"
$g.TextRenderingHint = "AntiAliasGridFit"

$rect = New-Object System.Drawing.Rectangle 0, 0, 256, 256
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(58, 40, 26)), ([System.Drawing.Color]::FromArgb(20, 14, 10)), 90
$g.FillRectangle($bg, $rect)

$gold = [System.Drawing.Color]::FromArgb(222, 178, 92)
$border = New-Object System.Drawing.Pen $gold, 6
$g.DrawRectangle($border, 9, 9, 238, 238)

$font = New-Object System.Drawing.Font "Georgia", 92, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = "Center"
$fmt.LineAlignment = "Center"
$g.DrawString("KW", $font, (New-Object System.Drawing.SolidBrush $gold), (New-Object System.Drawing.RectangleF 0, 22, 256, 130), $fmt)

# Bar track, settled XP (dark), gained XP (bright).
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(12, 9, 6))), 36, 170, 184, 34)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 104, 36))), 40, 174, 96, 26)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 120))), 136, 174, 52, 26)
$g.DrawRectangle((New-Object System.Drawing.Pen $gold, 2), 36, 170, 184, 34)

$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Wrote $out"
