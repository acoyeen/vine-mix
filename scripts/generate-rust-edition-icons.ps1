[CmdletBinding()]
param()
# Generates the "Rust edition" icons: the VineMix VM mark in white on a rounded
# orange tile, so a Rust build is told apart from 0.5 on the taskbar and in the
# notification area. Built only with the `rust-edition-icon` cargo feature.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$directory = Join-Path $PSScriptRoot '..\crates\vinemix-ui\assets\branding'
# Keep these two paths in sync with generate-brand-assets.ps1.
$upper = 'M2,0 H21 Q22,0 23,1 L50,28 L77,1 Q78,0 79,0 H98 Q101,0 99,3 L52,50 Q50,52 48,50 L1,3 Q-1,0 2,0 Z'
$lower = 'M3,24 Q3,21 5,23 L50,68 L95,23 Q97,21 97,24 V99 Q97,101 95,101 H81 Q79,101 79,99 V60 L52,87 Q50,89 48,87 L21,60 V99 Q21,101 19,101 H5 Q3,101 3,99 Z'
$converter = [Windows.Media.BrushConverter]::new()
$tile = $converter.ConvertFromString('#E8622C')
$ink = $converter.ConvertFromString('#FFFFFF')
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $inset = [Math]::Max(0.5, $size * 0.04)
    $side = $size - 2 * $inset
    $radius = $side * 0.22
    $drawing.DrawRoundedRectangle($tile, $null, [Windows.Rect]::new($inset, $inset, $side, $side), $radius, $radius)
    $scale = $side * 0.60 / 102.0
    $drawing.PushTransform([Windows.Media.TranslateTransform]::new(($size - 100 * $scale) / 2, ($size - 102 * $scale) / 2))
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
    $drawing.DrawGeometry($ink, $null, [Windows.Media.Geometry]::Parse($upper))
    $drawing.DrawGeometry($ink, $null, [Windows.Media.Geometry]::Parse($lower))
    $drawing.Pop(); $drawing.Pop(); $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new(); $encoder.Save($stream)
    $frames += ,@($size, $stream.ToArray()); $stream.Dispose()
}
foreach ($name in @('app-rust.ico', 'tray-rust.ico')) {
    $stream = [IO.File]::Create((Join-Path $directory $name))
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $edge = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
        $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
        $offset += $frame[1].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
    $writer.Dispose()
}
[IO.File]::WriteAllBytes((Join-Path $directory 'app-rust.png'), $frames[-1][1])
Write-Output 'Generated the Rust edition application and tray icons.'
