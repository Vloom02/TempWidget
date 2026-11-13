# Draws the app icon (a glossy Aero orb with a thermometer) and writes app.ico with PNG frames.
# Run from the project folder: powershell -File herramientas\icono.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot) "app.ico"

function Frame([int]$s) {
    $b = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'
    $f = $s / 256.0

    # Orb: sky blue at the top, sea green at the bottom
    $orb = New-Object System.Drawing.RectangleF (8 * $f), (8 * $f), (240 * $f), (240 * $f)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath; $path.AddEllipse($orb)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $orb, ([System.Drawing.Color]::FromArgb(255, 120, 214, 255)), ([System.Drawing.Color]::FromArgb(255, 30, 160, 120)), 90.0
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(255, 125, 215, 255), [System.Drawing.Color]::FromArgb(255, 28, 130, 220), [System.Drawing.Color]::FromArgb(255, 40, 180, 120))
    $blend.Positions = @(0.0, 0.6, 1.0)
    $fill.InterpolationColors = $blend
    $g.FillPath($fill, $path)
    $g.DrawEllipse((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(220, 255, 255, 255)), ([Math]::Max(1, 6 * $f))), $orb)

    # Glass highlight across the top half
    $hl = New-Object System.Drawing.RectangleF (40 * $f), (18 * $f), (176 * $f), (104 * $f)
    $hb = New-Object System.Drawing.Drawing2D.LinearGradientBrush $hl, ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(20, 255, 255, 255)), 90.0
    $g.FillEllipse($hb, $hl)

    # Thermometer: white tube and bulb, warm mercury inside
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $warm = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 92, 72))
    $tube = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tw = 44 * $f; $tx = 128 * $f - $tw / 2; $ty = 52 * $f; $th = 120 * $f
    $tube.AddArc($tx, $ty, $tw, $tw, 180, 180); $tube.AddLine($tx + $tw, $ty + $tw / 2, $tx + $tw, $ty + $th); $tube.AddLine($tx, $ty + $th, $tx, $ty + $tw / 2); $tube.CloseFigure()
    $g.FillPath($white, $tube)
    $g.FillEllipse($white, 128 * $f - 40 * $f, 150 * $f, 80 * $f, 80 * $f)
    $g.FillEllipse($warm, 128 * $f - 28 * $f, 162 * $f, 56 * $f, 56 * $f)
    $g.FillRectangle($warm, 128 * $f - 10 * $f, 100 * $f, 20 * $f, 72 * $f)

    $g.Dispose()
    return , (Encode $b)
}

# 256 px goes in as PNG; smaller sizes as a classic 32-bit DIB, which every Windows API and older .NET can read.
function Encode([System.Drawing.Bitmap]$b) {
    $s = $b.Width
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) { $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); return , $ms.ToArray() }
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32](2 * $s))   # BITMAPINFOHEADER: height counts the AND mask too
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0); $bw.Write([uint32]0)
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $data = $b.LockBits($rect, 'ReadOnly', [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object byte[] ($data.Stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length); $b.UnlockBits($data)
    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $data.Stride, $s * 4) }   # bottom-up rows, BGRA
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $s)))                                   # AND mask unused: alpha decides
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$frames = $sizes | ForEach-Object { , (Frame $_) }
$fs = [System.IO.File]::Create($out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                   # ICONDIRENTRY per size
    $s = $sizes[$i]; $len = $frames[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)  # 256 is written as 0
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write([byte[]]$f) }
$w.Close()
"app.ico: $((Get-Item $out).Length) bytes, sizes $($sizes -join ', ')"
