# Generates PowerTray.ico (16/32/48/256) from code, so the asset is reproducible.
# Design: IEC power glyph, white on a dark rounded square.
# Run:  powershell -ExecutionPolicy Bypass -File make-icon.ps1

Add-Type -AssemblyName System.Drawing

$sizes = 16, 32, 48, 256
$outIco = Join-Path $PSScriptRoot 'PowerTray.ico'

function New-IconBitmap([int]$S) {
    $bmp = New-Object Drawing.Bitmap($S, $S, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $k = $S / 64.0

    # rounded-square background
    $d = 14.0 * $k * 2
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($S - $d, 0, $d, $d, 270, 90)
    $path.AddArc($S - $d, $S - $d, $d, $d, 0, 90)
    $path.AddArc(0, $S - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 31, 42, 55))
    $g.FillPath($bg, $path)

    # power glyph: broken ring + vertical bar through the gap
    $pen = New-Object Drawing.Pen([Drawing.Color]::White, (6.0 * $k))
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $cx = 32.0 * $k
    $cy = 34.0 * $k
    $rad = 18.0 * $k
    $g.DrawArc($pen, ($cx - $rad), ($cy - $rad), ($rad * 2), ($rad * 2), -55, 290)
    $g.DrawLine($pen, $cx, (11.0 * $k), $cx, (31.0 * $k))

    $pen.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# 32bpp bottom-up DIB with an empty AND mask. Used for 16/32/48 because
# PNG-compressed entries at small sizes upset some older shell/tooling paths.
function Get-DibBytes([Drawing.Bitmap]$bmp) {
    $W = $bmp.Width
    $H = $bmp.Height
    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter($ms)
    $bw.Write([int]40)
    $bw.Write([int]$W)
    $bw.Write([int]($H * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([int]0)
    $bw.Write([int]($W * $H * 4))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $H - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $W; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = [int][Math]::Floor(($W + 31) / 32) * 4
    $bw.Write((New-Object byte[] ($maskRow * $H)))
    $bw.Flush()
    return , $ms.ToArray()   # leading comma stops PowerShell unrolling the byte[]
}

function Get-PngBytes([Drawing.Bitmap]$bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$entries = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) { $data = Get-PngBytes $bmp } else { $data = Get-DibBytes $bmp }
    $entries += [pscustomobject]@{ Size = $s; Data = $data }
    $bmp.Save((Join-Path $PSScriptRoot "preview-$s.png"), [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$fs = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter($fs)
$bw.Write([uint16]0)                  # reserved
$bw.Write([uint16]1)                  # type: icon
$bw.Write([uint16]$entries.Count)

$offset = 6 + (16 * $entries.Count)
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }   # 0 means 256 in the ICO spec
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([int]$e.Data.Length)
    $bw.Write([int]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write([byte[]]$e.Data) }
$bw.Flush()
[IO.File]::WriteAllBytes($outIco, $fs.ToArray())

Write-Host ("Wrote {0} ({1} bytes, {2} sizes)" -f $outIco, (Get-Item $outIco).Length, $entries.Count)
