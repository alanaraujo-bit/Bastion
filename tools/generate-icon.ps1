# Renders the Bastion emblem (shield + keyhole) at multiple sizes and assembles
# a multi-resolution .ico. Output: assets\bastion.ico
param([string]$Out = "C:\Users\Alan Araujo\Documents\Bastion\assets\bastion.ico")

Add-Type -AssemblyName System.Drawing

function New-Emblem([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 48.0

    function P([double]$x, [double]$y) { New-Object System.Drawing.PointF (($x*$s), ($y*$s)) }

    # Shield
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddLine((P 24 3), (P 41 9.5))
    $path.AddLine((P 41 9.5), (P 41 24.5))
    $path.AddBezier((P 41 24.5), (P 41 35.5), (P 33.6 42.6), (P 24 45.6))
    $path.AddBezier((P 24 45.6), (P 14.4 42.6), (P 7 35.5), (P 7 24.5))
    $path.AddLine((P 7 24.5), (P 7 9.5))
    $path.CloseFigure()

    $accent = [System.Drawing.Color]::FromArgb(255, 91, 91, 214)   # #5B5BD6
    $brush = New-Object System.Drawing.SolidBrush $accent
    $g.FillPath($brush, $path)

    # subtle right-side sheen
    $sheenPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $sheenPath.AddLine((P 24 3), (P 41 9.5))
    $sheenPath.AddLine((P 41 9.5), (P 41 24.5))
    $sheenPath.AddBezier((P 41 24.5), (P 41 35.5), (P 33.6 42.6), (P 24 45.6))
    $sheenPath.AddLine((P 24 45.6), (P 24 3))
    $sheenPath.CloseFigure()
    $sheen = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28, 255, 255, 255))
    $g.FillPath($sheen, $sheenPath)

    # Keyhole (white)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $g.FillEllipse($white, (19*$s), (14*$s), (10*$s), (10*$s))
    $stem = @((P 22 20), (P 26 20), (P 27.6 32), (P 20.4 32))
    $g.FillPolygon($white, $stem)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-Emblem $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
}

$dir = Split-Path $Out -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
# ICONDIR
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $bytes = $pngs[$i]
    $wb = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([Byte]$wb); $bw.Write([Byte]$wb)   # width,height
    $bw.Write([Byte]0); $bw.Write([Byte]0)       # colors,reserved
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)  # planes,bpp
    $bw.Write([UInt32]$bytes.Length)             # size
    $bw.Write([UInt32]$offset)                   # offset
    $offset += $bytes.Length
}
foreach ($bytes in $pngs) { $bw.Write($bytes) }
$bw.Flush(); $fs.Close()
Write-Output ("ICO written: " + $Out + " (" + ((Get-Item $Out).Length) + " bytes)")
