# Renders the Nimbus icon (frosted-glass cloud on a royal-blue squircle) into a multi-size
# Nimbus.ico plus a 256px Nimbus.png for use inside the app.
# Usage (from the repo root): .\tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\Nimbus\Assets"
$sizes = 16, 24, 32, 48, 64, 128, 256

function Argb([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Pt([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }

# Superellipse ("squircle") like modern app icons, instead of a plain rounded rectangle.
function New-Squircle([float]$cx, [float]$cy, [float]$r, [float]$n) {
    $pts = for ($i = 0; $i -lt 720; $i++) {
        $t = $i * [Math]::PI / 360
        $c = [Math]::Cos($t); $s = [Math]::Sin($t)
        Pt ($cx + $r * [Math]::Sign($c) * [Math]::Pow([Math]::Abs($c), 2 / $n)) ($cy + $r * [Math]::Sign($s) * [Math]::Pow([Math]::Abs($s), 2 / $n))
    }
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddPolygon([System.Drawing.PointF[]]$pts)
    return $p
}

function Get-Angle([float]$cx, [float]$cy, $pt) {
    $a = [Math]::Atan2($pt[1] - $cy, $pt[0] - $cx) * 180 / [Math]::PI
    if ($a -lt 0) { $a += 360 }
    return $a
}

# The upper of the two points where circles 1 and 2 cross.
function Get-UpperIntersection($c1, $c2) {
    $dx = $c2[0] - $c1[0]; $dy = $c2[1] - $c1[1]
    $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
    $a = ($c1[2] * $c1[2] - $c2[2] * $c2[2] + $d * $d) / (2 * $d)
    $h = [Math]::Sqrt($c1[2] * $c1[2] - $a * $a)
    $bx = $c1[0] + $a * $dx / $d; $by = $c1[1] + $a * $dy / $d
    $p1 = @(($bx + $h * $dy / $d), ($by - $h * $dx / $d))
    $p2 = @(($bx - $h * $dy / $d), ($by + $h * $dx / $d))
    if ($p1[1] -lt $p2[1]) { return , $p1 } else { return , $p2 }
}

# Big dome flanked by two puffs, all sitting on one flat bottom edge (y = 172.5).
# Built as a single outline (arc, arc, arc, flat bottom) so strokes follow the silhouette only.
function New-Cloud {
    $left = @(81.7, 140.9, 31.6); $dome = @(127.0, 121.9, 47.3); $right = @(173.5, 141.9, 30.6)
    $ld = Get-UpperIntersection $left $dome
    $dr = Get-UpperIntersection $dome $right

    $leftEnd = Get-Angle $left[0] $left[1] $ld
    $domeStart = Get-Angle $dome[0] $dome[1] $ld
    $domeEnd = Get-Angle $dome[0] $dome[1] $dr
    $rightStart = Get-Angle $right[0] $right[1] $dr

    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(($left[0] - $left[2]), ($left[1] - $left[2]), (2 * $left[2]), (2 * $left[2]), 90, ($leftEnd - 90))
    $p.AddArc(($dome[0] - $dome[2]), ($dome[1] - $dome[2]), (2 * $dome[2]), (2 * $dome[2]), $domeStart, ($domeEnd - $domeStart))
    $p.AddArc(($right[0] - $right[2]), ($right[1] - $right[2]), (2 * $right[2]), (2 * $right[2]), $rightStart, (450 - $rightStart))
    $p.CloseFigure()
    return $p
}

# If tools\icon-source.png exists (the official artwork), it is used instead of the drawn icon:
# scaled down per size and cut to the squircle so the corners are transparent.
$sourcePath = Join-Path $PSScriptRoot "icon-source.png"
$source = if (Test-Path $sourcePath) { [System.Drawing.Image]::FromFile((Resolve-Path $sourcePath).Path) } else { $null }

function Render-FromSource([int]$size) {
    $scaled = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($scaled)
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.DrawImage($source, 0, 0, $size, $size)
    $sg.Dispose()

    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.TextureBrush $scaled
    $g.FillPath($brush, (New-Squircle ($size / 2) ($size / 2) ($size * 0.495) 4.2))
    $g.Dispose(); $scaled.Dispose()
    return $bmp
}

function Render([int]$size) {
    if ($source) { return Render-FromSource $size }
    $small = $size -le 32
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    # Designed on a 256x256 canvas and scaled down.
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    # Background: bright top-right fading to deep blue bottom-left, darker towards the rim.
    $bg = New-Squircle 128 128 120 4.2
    $base = New-Object System.Drawing.Drawing2D.LinearGradientBrush (Pt 236 12), (Pt 20 244), (Argb 255 52 136 255), (Argb 255 6 34 196)
    $g.FillPath($base, $bg)
    $vignette = New-Object System.Drawing.Drawing2D.PathGradientBrush $bg
    $vignette.CenterColor = Argb 0 0 16 110
    $vignette.SurroundColors = [System.Drawing.Color[]]@((Argb 120 0 14 100))
    $vignette.FocusScales = Pt 0.72 0.72
    $g.FillPath($vignette, $bg)

    $cloud = New-Cloud

    # Soft halo around the cloud.
    if (-not $small) {
        for ($i = 6; $i -ge 1; $i--) {
            $pen = New-Object System.Drawing.Pen (Argb 7 160 205 255), ($i * 3)
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $cloud)
        }
    }

    # Frosted glass body: near-white at the top, clear blue at the bottom.
    $bottom = if ($small) { Argb 255 178 212 255 } else { Argb 225 64 138 250 }
    $body = New-Object System.Drawing.Drawing2D.LinearGradientBrush (Pt 0 74), (Pt 0 173), (Argb 252 242 249 255), $bottom
    $g.FillPath($body, $cloud)

    if (-not $small) {
        $g.SetClip($cloud)

        # Inner rim light, strongest right at the edge.
        foreach ($w in @(@(14, 22), @(7, 45), @(3, 110))) {
            $pen = New-Object System.Drawing.Pen (Argb $w[1] 255 255 255), $w[0]
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $cloud)
        }

        # Glossy highlight across the upper-left of the dome and left puff.
        $gloss = New-Object System.Drawing.Drawing2D.GraphicsPath
        $gloss.AddBezier((Pt 60 150), (Pt 72 128), (Pt 90 120), (Pt 104 114))
        $gloss.AddBezier((Pt 104 114), (Pt 112 98), (Pt 124 84), (Pt 140 76))
        $gloss.AddLine((Pt 140 76), (Pt 40 60))
        $gloss.AddLine((Pt 40 60), (Pt 40 150))
        $gloss.CloseFigure()
        $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush (Pt 70 80), (Pt 105 125), (Argb 110 255 255 255), (Argb 0 255 255 255)
        $g.FillPath($shine, $gloss)

        $g.ResetClip()
    }

    $g.Dispose()
    return $bmp
}

function To-Png($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

New-Item -ItemType Directory -Force $assets | Out-Null
$assets = (Resolve-Path $assets).Path

$images = foreach ($s in $sizes) {
    $bmp = Render $s
    if ($s -eq 256) { $bmp.Save("$assets\Nimbus.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    , (To-Png $bmp)
    $bmp.Dispose()
}

# ICO container with PNG-compressed frames (supported since Windows Vista).
$fs = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes("$assets\Nimbus.ico", $fs.ToArray())
Write-Output "Wrote $assets\Nimbus.ico ($($fs.Length) bytes) and Nimbus.png"
