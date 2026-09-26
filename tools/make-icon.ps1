param(
    [string]$OutPath = "D:\Opencode\GameBoost\src\GameBoost.App\Assets\GameBoost.ico"
)
Add-Type -AssemblyName System.Drawing

function New-IconFile([int[]]$sizes, [string]$outPath) {
    $blobs = New-Object System.Collections.Generic.List[byte[]]
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.PixelOffsetMode = 'HighQuality'
        $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
        $pad = [int]($s * 0.04)
        $r = [int]($s * 0.20)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($pad, $pad, $r * 2, $r * 2, 180, 90)
        $path.AddArc($s - $pad - $r * 2, $pad, $r * 2, $r * 2, 270, 90)
        $path.AddArc($s - $pad - $r * 2, $s - $pad - $r * 2, $r * 2, $r * 2, 0, 90)
        $path.AddArc($pad, $s - $pad - $r * 2, $r * 2, $r * 2, 90, 90)
        $path.CloseFigure()
        $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 16, 22, 30), [System.Drawing.Color]::FromArgb(255, 12, 66, 40), 45)
        $g.FillPath($bg, $path)
        $penW = [float][Math]::Max(1.0, $s * 0.035)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 34, 197, 94), $penW)
        $g.DrawPath($pen, $path)

        $bolt = New-Object System.Drawing.Drawing2D.GraphicsPath
        $pts = @(
            (New-Object System.Drawing.PointF([float]($s * 0.58), [float]($s * 0.12))),
            (New-Object System.Drawing.PointF([float]($s * 0.27), [float]($s * 0.56))),
            (New-Object System.Drawing.PointF([float]($s * 0.46), [float]($s * 0.56))),
            (New-Object System.Drawing.PointF([float]($s * 0.38), [float]($s * 0.88))),
            (New-Object System.Drawing.PointF([float]($s * 0.73), [float]($s * 0.43))),
            (New-Object System.Drawing.PointF([float]($s * 0.53), [float]($s * 0.43)))
        )
        $bolt.AddPolygon($pts)
        $green = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 74, 222, 128), [System.Drawing.Color]::FromArgb(255, 16, 185, 129), 90)
        $g.FillPath($green, $bolt)
        $g.Dispose()

        $msImg = New-Object System.IO.MemoryStream
        $bmp.Save($msImg, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $blobs.Add($msImg.ToArray())
        $msImg.Dispose()
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $w = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$w)
        $bw.Write([byte]$w)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$blobs[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $blobs[$i].Length
    }
    foreach ($b in $blobs) { $bw.Write($b) }
    $bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    [System.IO.File]::WriteAllBytes($outPath, $bytes)
    return $bytes
}

$bytes = New-IconFile @(16, 24, 32, 48, 64, 128, 256) $OutPath

$icon = New-Object System.Drawing.Icon($OutPath)
$bmp = $icon.ToBitmap()
Write-Output ("OK System.Drawing: {0}x{1}" -f $bmp.Width, $bmp.Height)
$bmp.Dispose()
$icon.Dispose()

$fs = [System.IO.File]::OpenRead($OutPath)
$br = New-Object System.IO.BinaryReader($fs)
$null = $br.ReadUInt16(); $type = $br.ReadUInt16(); $count = $br.ReadUInt16()
$ok = $true
for ($i = 0; $i -lt $count; $i++) {
    $w = $br.ReadByte(); $h = $br.ReadByte(); $null = $br.ReadByte(); $null = $br.ReadByte()
    $null = $br.ReadUInt16(); $null = $br.ReadUInt16()
    $size = $br.ReadUInt32(); $off = $br.ReadUInt32()
    if (($off + $size) -gt $bytes.Length) { $ok = $false; Write-Output "BAD entry $i : off=$off size=$size total=$($bytes.Length)" }
    $fs.Position = $fs.Position
}
$br.Close()
Write-Output ("Structure: type={0} count={1} valid={2} length={3}" -f $type, $count, $ok, $bytes.Length)
