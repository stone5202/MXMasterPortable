# Generates src\app.ico (multi-size) and src\app.png (256 px logo) for MX Master WIN.
# Uses only System.Drawing from the in-box .NET Framework; no external tools.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$icoPath = Join-Path $projectRoot 'src\app.ico'
$pngPath = Join-Path $projectRoot 'src\app.png'

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Draws the artwork on a 256-unit design grid. The compact variant uses bolder
# shapes so the 16-24 px frames stay readable in the taskbar and tray.
function New-Artwork([int]$pixels, [bool]$compact) {
    $bitmap = New-Object System.Drawing.Bitmap($pixels, $pixels, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($pixels / 256.0, $pixels / 256.0)

    if ($compact) { $bg = New-RoundedPath 2 2 252 252 58 } else { $bg = New-RoundedPath 12 12 232 232 54 }
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(256, 256)),
        [System.Drawing.Color]::FromArgb(255, 18, 194, 168), [System.Drawing.Color]::FromArgb(255, 30, 92, 232))
    $g.FillPath($gradient, $bg)

    if ($compact) {
        $body = New-RoundedPath 72 30 112 196 56
        $wheel = New-RoundedPath 114 62 28 60 14
    } else {
        $shadow = New-RoundedPath 82 54 92 164 46
        $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(45, 8, 30, 80))
        $g.FillPath($shadowBrush, $shadow)
        $shadowBrush.Dispose(); $shadow.Dispose()
        $body = New-RoundedPath 82 46 92 164 46
        $wheel = New-RoundedPath 119 74 18 46 9
    }
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $body)
    $wheelBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 22, 110, 215))
    $g.FillPath($wheelBrush, $wheel)

    $white.Dispose(); $wheelBrush.Dispose(); $gradient.Dispose()
    $bg.Dispose(); $body.Dispose(); $wheel.Dispose(); $g.Dispose()
    return $bitmap
}

function Resize-Bitmap([System.Drawing.Bitmap]$source, [int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $attributes = New-Object System.Drawing.Imaging.ImageAttributes
    $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $target = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $g.DrawImage($source, $target, 0, 0, $source.Width, $source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    $attributes.Dispose(); $g.Dispose()
    return $bitmap
}

# Classic 32-bit DIB frame (XOR bitmap + empty AND mask), readable by every Windows icon consumer.
function Get-DibFrame([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    for ($row = 0; $row -lt $size; $row++) {
        $sourceRow = [IntPtr]($data.Scan0.ToInt64() + $row * $data.Stride)
        [System.Runtime.InteropServices.Marshal]::Copy($sourceRow, $pixels, ($size - 1 - $row) * $size * 4, $size * 4)
    }
    $bitmap.UnlockBits($data)

    $maskStride = [int]([Math]::Floor(($size + 31) / 32)) * 4
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32]($pixels.Length + $maskStride * $size))
    $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
    $writer.Write($pixels)
    $writer.Write((New-Object byte[] ($maskStride * $size)))
    $writer.Flush()
    return ,$stream.ToArray()
}

function Get-PngFrame([System.Drawing.Bitmap]$bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$stream.ToArray()
}

$masterFull = New-Artwork 1024 $false
$masterCompact = New-Artwork 1024 $true

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = @()
foreach ($size in $sizes) {
    if ($size -le 24) { $master = $masterCompact } else { $master = $masterFull }
    $frame = Resize-Bitmap $master $size
    if ($size -eq 256) {
        $frame.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = Get-PngFrame $frame
    } else {
        $bytes = Get-DibFrame $frame
    }
    $frames += ,@{ Size = $size; Bytes = $bytes }
    $frame.Dispose()
}
$masterFull.Dispose(); $masterCompact.Dispose()

$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($output)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $output.ToArray())

Write-Host "Icon written: $icoPath ($($frames.Count) frames)"
Write-Host "Logo written: $pngPath"
