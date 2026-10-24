param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\Launcher\Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
function New-NexusIconBitmap([int]$Size) {
    $bmp = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($Size / 256.0, $Size / 256.0)
    $navy = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 12, 26, 38))
    $rim = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 54, 83, 102), 5)
    $cyan = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 89, 216, 229), 14)
    $amber = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 244, 174, 89), 14)
    $white = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 225, 245, 249), 16)
    $cyan.StartCap = $cyan.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $amber.StartCap = $amber.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $white.StartCap = $white.EndCap = [Drawing.Drawing2D.LineCap]::Square
    $white.LineJoin = [Drawing.Drawing2D.LineJoin]::Miter
    $g.Clear([Drawing.Color]::Transparent)
    $g.FillEllipse($navy, 8, 8, 240, 240)
    $g.DrawEllipse($rim, 10, 10, 236, 236)
    $g.DrawArc($cyan, 29, 29, 198, 198, 54, 278)
    $g.DrawArc($amber, 29, 29, 198, 198, -12, 38)
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(79,175), [Drawing.PointF]::new(128,73), [Drawing.PointF]::new(177,175))
    $g.DrawLines($white, $points)
    $g.DrawLine($cyan, 102,151,154,151)
    $navy.Dispose(); $rim.Dispose(); $cyan.Dispose(); $amber.Dispose(); $white.Dispose(); $g.Dispose()
    return $bmp
}
$png = New-NexusIconBitmap 512
$png.Save((Join-Path $OutputDirectory 'Launcher.png'), [Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
$frames = foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-NexusIconBitmap $size
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $frame = [pscustomobject]@{ Size=$size; Bytes=$stream.ToArray() }
    $stream.Dispose(); $bitmap.Dispose()
    $frame
}
$iconStream = [IO.File]::Create((Join-Path $OutputDirectory 'Launcher.ico'))
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$frames.Count)
    $offset = 6 + ($frames.Count * 16)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.Bytes.Length); $writer.Write([UInt32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $iconStream.Dispose() }
Write-Output 'Created original AO Nexus icon: Launcher.png and seven-size Launcher.ico.'
