<#
.SYNOPSIS
  Builds the app's .ico files from the design assets in design\ (see design\design.md).

  src\TableSnip\Assets\app.ico         the filled amber tile at 16-256 px (window, taskbar, installer)
  src\TableSnip\Assets\tray-white.ico  single-colour glyph for dark taskbars (16/20/24/32 px)
  src\TableSnip\Assets\tray-black.ico  single-colour glyph for light taskbars
  docs\icon.png                        the filled tile at 256 px
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot
$design = Join-Path $root "design"
$assets = Join-Path $root "src\TableSnip\Assets"

function Resize([System.Drawing.Image]$img, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $bmp.SetResolution(96, 96)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($img, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

# Writes an .ico whose entries are PNG-compressed (supported since Windows Vista).
function WriteIco([string]$path, [object[]]$entries) {
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $s = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([byte]$s); $bw.Write([byte]$s); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$e.Bytes.Length); $bw.Write([UInt32]$offset)
        $offset += $e.Bytes.Length
    }
    foreach ($e in $entries) { $bw.Write([byte[]]$e.Bytes) }
    $bw.Dispose(); $fs.Dispose()
    Write-Host ("wrote {0} ({1} sizes, {2:N0} bytes)" -f $path, $entries.Count, (Get-Item $path).Length)
}

# --- app icon: the filled amber tile
$tile = [System.Drawing.Image]::FromFile((Join-Path $design "app-icon-filled-512.png"))
$entries = @()
foreach ($s in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
    $b = Resize $tile $s
    $entries += @{ Size = $s; Bytes = (PngBytes $b) }
    $b.Dispose()
}
WriteIco (Join-Path $assets "app.ico") $entries
$tile.Dispose()

# --- tray icons: the single-colour glyphs; 20 px (125% DPI) is resized from the 32 px one
foreach ($colour in "white", "black") {
    $entries = @()
    foreach ($s in 16, 20, 24, 32) {
        $file = Join-Path $design "tray-mono-$colour-$s.png"
        if (Test-Path $file) {
            $entries += @{ Size = $s; Bytes = [System.IO.File]::ReadAllBytes($file) }
        } else {
            $big = [System.Drawing.Image]::FromFile((Join-Path $design "tray-mono-$colour-32.png"))
            $b = Resize $big $s
            $entries += @{ Size = $s; Bytes = (PngBytes $b) }
            $b.Dispose(); $big.Dispose()
        }
    }
    WriteIco (Join-Path $assets "tray-$colour.ico") $entries
}

Copy-Item (Join-Path $design "app-icon-filled-256.png") (Join-Path $root "docs\icon.png") -Force
Write-Host "wrote docs\icon.png"
