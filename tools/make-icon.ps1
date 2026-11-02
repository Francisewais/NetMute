# Draws src\app.ico: a globe with a slash through it, on a blue tile.
# Sizes up to 64 are stored as plain bitmaps, 256 as PNG, as Windows expects.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\app.ico'))

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Floor($s * 0.03)
    $rect = New-Object System.Drawing.RectangleF($inset, $inset, ($s - 2 * $inset), ($s - 2 * $inset))
    $d = $s * 0.42
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $tile.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $tile.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $tile.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $tile.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(58, 123, 240)
    $bottom = [System.Drawing.Color]::FromArgb(27, 73, 190)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90.0)
    $g.FillPath($fill, $tile)

    $c = $s / 2.0
    $r = $s * 0.29
    $w = [Math]::Max(1.0, $s * 0.055)
    $white = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $w)
    $g.DrawEllipse($white, ($c - $r), ($c - $r), (2 * $r), (2 * $r))
    if ($s -ge 32) {
        $mr = $r * 0.42
        $g.DrawEllipse($white, ($c - $mr), ($c - $r), (2 * $mr), (2 * $r))
        $g.DrawLine($white, ($c - $r), $c, ($c + $r), $c)
    }

    $a = $s * 0.19
    $b = $s * 0.81
    $halo = New-Object System.Drawing.Pen($bottom, ($w * 3.2))
    $halo.StartCap = $halo.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($halo, $a, $a, $b, $b)
    $red = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 92, 92), [Math]::Max(1.5, $w * 1.8))
    $red.StartCap = $red.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($red, $a, $a, $b, $b)

    $g.Dispose()
    return $bmp
}

function Get-DibBytes($bmp) {
    $s = $bmp.Width
    $area = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($area, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($s * $s * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskRow = [int]([Math]::Floor(($s + 31) / 32) * 4)
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($out)
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2))
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($pixels.Length + $maskRow * $s))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $s * 4, $s * 4) }
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    return ,$out.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -eq 256) {
        $png = New-Object System.IO.MemoryStream
        $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        ,$png.ToArray()
    } else {
        Get-DibBytes $bmp
    }
    $bmp.Dispose()
}

$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($file)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write([byte[]]$image) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Out, $file.ToArray())
Write-Output ("wrote {0} ({1} bytes)" -f $Out, $file.Length)
