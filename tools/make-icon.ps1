param(
    [string]$Src = "..\src\xDocHunter\Assets\Icons\app-icon.png",
    [string]$Dst = "..\src\xDocHunter\Assets\Icons\app-icon.ico"
)

Add-Type -AssemblyName System.Drawing

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcPath = Join-Path $scriptDir $Src | Resolve-Path
$dstPath = Join-Path $scriptDir $Dst

$sizes = @(16, 24, 32, 48, 64, 128, 256)

# Load source
$srcBmp = [System.Drawing.Bitmap]::FromFile($srcPath)

# Render each size into a PNG byte[]
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($srcBmp, 0, 0, $s, $s)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += ,(@{ Size = $s; Bytes = $ms.ToArray() })
    $ms.Dispose()
}
$srcBmp.Dispose()

# Build ICO
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out

# ICONDIR
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: 1 = icon
$bw.Write([uint16]$pngs.Count)       # count

$headerSize = 6 + (16 * $pngs.Count)
$offset = $headerSize
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { [byte]0 } else { [byte]$p.Size }
    $bw.Write($dim)                  # width
    $bw.Write($dim)                  # height
    $bw.Write([byte]0)               # palette count
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bpp
    $bw.Write([uint32]$p.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) {
    $bw.Write($p.Bytes)
}

[System.IO.File]::WriteAllBytes($dstPath, $out.ToArray())
$bw.Dispose()
$out.Dispose()

Write-Host "Wrote $dstPath ($((Get-Item $dstPath).Length) bytes)"
