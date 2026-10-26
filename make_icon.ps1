# Рисует значок Старт-пака: цветная «вертушка» из четырёх лепестков (цвета программ, которые он запускает)
# с белой кнопкой «пуск» в центре, на тёмном фоне.
# Результат: assets\app.ico (маленькие кадры BMP, 256 — PNG) и assets\app-256.png.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function C($hex, $a = 255) { $v = [Convert]::ToInt32($hex, 16); [System.Drawing.Color]::FromArgb($a, ($v -shr 16) -band 255, ($v -shr 8) -band 255, $v -band 255) }
function P($x, $y) { New-Object System.Drawing.PointF ([float]$x), ([float]$y) }
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($s / 100.0, $s / 100.0)
    $small = $s -le 24

    # фон: тёмный «сквиркл» (ночное небо), на нём яркие цвета видно сразу
    $bg = RoundRect 2 2 96 96 24
    $rect = New-Object System.Drawing.RectangleF 2, 2, 96, 96
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C '312E81'), (C '0B1026'), 60
    $g.FillPath($grad, $bg)
    if (-not $small) {
        $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C 'FFFFFF' 34), (C 'FFFFFF' 0), 90
        $g.SetClip($bg); $g.FillEllipse($hl, -20, -62, 140, 100); $g.ResetClip()
    }

    # вертушка: четыре лепестка цветов программ — красный, фиолетовый, голубой, зелёный
    $pairs = @(@('FF3D6E', 'FF8A3D'), @('8B5CF6', 'D946EF'), @('22D3EE', '3B82F6'), @('A3E635', '10B981'))
    $len = $(if ($small) { 44 } else { 42 }); $wid = $(if ($small) { 30 } else { 27 })
    for ($i = 0; $i -lt 4; $i++) {
        $st = $g.Save()
        $g.TranslateTransform(50, 50); $g.RotateTransform(45 + 90 * $i)
        # лепесток-капля: круглый снаружи, острый к центру, с закрученным краем
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddBezier((P 0 -4), (P ($wid * 0.95) -8), (P ($wid * 0.9) (-$len)), (P 0 (-$len)))
        $p.AddBezier((P 0 (-$len)), (P (-$wid * 0.55) (-$len)), (P (-$wid * 0.35) -14), (P 0 -4))
        $p.CloseFigure()
        $pr = New-Object System.Drawing.RectangleF (-$wid), (-$len), ($wid * 2), $len
        $pb = New-Object System.Drawing.Drawing2D.LinearGradientBrush $pr, (C $pairs[$i][0]), (C $pairs[$i][1]), 90
        $g.FillPath($pb, $p)
        $g.Restore($st)
    }

    # центр: белая кнопка «пуск»
    $r = $(if ($small) { 15 } else { 14 })
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C '0B1026' 90)), 50 - $r - 1, 50 - $r + 1.5, 2 * $r + 2, 2 * $r + 2)
    $g.FillEllipse([System.Drawing.Brushes]::White, 50 - $r, 50 - $r, 2 * $r, 2 * $r)
    $t = $r * 0.55
    $tri = [System.Drawing.PointF[]]@((P (50 - $t * 0.55) (50 - $t)), (P (50 - $t * 0.55) (50 + $t)), (P (50 + $t * 0.95) 50))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (C '312E81')), $tri)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Save((Join-Path $PSScriptRoot 'assets\app-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # 32-битный BMP: его понимают и Windows, и .NET (System.Drawing.Icon)
        $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', 'Format32bppArgb')
        $px = New-Object byte[] ($data.Stride * $s)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
        $bmp.UnlockBits($data)
        $mw = New-Object System.IO.BinaryWriter $ms
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $mw.Write([UInt32]40); $mw.Write([Int32]$s); $mw.Write([Int32]($s * 2)); $mw.Write([UInt16]1); $mw.Write([UInt16]32)
        $mw.Write([UInt32]0); $mw.Write([UInt32]($s * $s * 4 + $maskRow * $s)); $mw.Write([Int32]0); $mw.Write([Int32]0); $mw.Write([UInt32]0); $mw.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { $mw.Write($px, $y * $data.Stride, $s * 4) }
        $mw.Write((New-Object byte[] ($maskRow * $s)))
        $mw.Flush()
    }
    $bmp.Dispose()
    $frames += , $ms.ToArray()
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close()
Write-Host "Значок: $out"
