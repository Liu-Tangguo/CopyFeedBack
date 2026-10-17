# 生成多尺寸应用图标 resources/app.ico（以及 256px 预览图，供文档使用）
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\resources\app.ico'),
    [string]$PreviewPath = (Join-Path $PSScriptRoot '..\resources\app-icon-256.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$pngList = New-Object System.Collections.ArrayList

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $margin = [single]($size * 0.03)
    $rect = New-Object System.Drawing.RectangleF($margin, $margin, [single]($size - 2 * $margin), [single]($size - 2 * $margin))
    $radius = [single]([Math]::Max(2.0, $size * 0.26))
    $d = $radius * 2

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 59, 130, 246)
    $c2 = [System.Drawing.Color]::FromArgb(255, 29, 78, 216)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45)
    $g.FillPath($grad, $path)

    $stroke = [single]([Math]::Max(1.4, $size * 0.085))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $points = @(
        (New-Object System.Drawing.PointF([single]($size * 0.27), [single]($size * 0.52))),
        (New-Object System.Drawing.PointF([single]($size * 0.43), [single]($size * 0.67))),
        (New-Object System.Drawing.PointF([single]($size * 0.74), [single]($size * 0.34)))
    )
    $g.DrawLines($pen, $points)

    $pen.Dispose()
    $grad.Dispose()
    $path.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [void]$pngList.Add($ms.ToArray())
    $ms.Dispose()

    if ($size -eq 256) {
        $dir = [System.IO.Path]::GetDirectoryName($PreviewPath)
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $bmp.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
}

$dir = [System.IO.Path]::GetDirectoryName($OutPath)
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$stream = [System.IO.File]::Create($OutPath)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)                      # reserved
$writer.Write([UInt16]1)                      # type: icon
$writer.Write([UInt16]$sizes.Count)           # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([Byte]$dim)                 # width
    $writer.Write([Byte]$dim)                 # height
    $writer.Write([Byte]0)                    # palette count
    $writer.Write([Byte]0)                    # reserved
    $writer.Write([UInt16]1)                  # color planes
    $writer.Write([UInt16]32)                 # bits per pixel
    $bytes = [byte[]]$pngList[$i]
    $writer.Write([UInt32]$bytes.Length)      # size in bytes
    $writer.Write([UInt32]$offset)            # offset
    $offset += $bytes.Length
}
foreach ($bytes in $pngList) {
    $writer.Write([byte[]]$bytes)
}
$writer.Flush()
$writer.Dispose()
$stream.Dispose()

Write-Output ("ICO 已生成: " + $OutPath)
