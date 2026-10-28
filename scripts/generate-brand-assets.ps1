[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$directory = Join-Path $PSScriptRoot '..\desktop\VineMix.Desktop\Assets\Branding'
$null = New-Item -ItemType Directory -Path $directory -Force
# Keep these two paths in sync with Controls/BrandMark.cs. Their long diagonals
# share a 45-degree angle; the two filled pieces remain separated at every size.
$upper = 'M2,0 H21 Q22,0 23,1 L50,28 L77,1 Q78,0 79,0 H98 Q101,0 99,3 L52,50 Q50,52 48,50 L1,3 Q-1,0 2,0 Z'
$lower = 'M3,24 Q3,21 5,23 L50,68 L95,23 Q97,21 97,24 V99 Q97,101 95,101 H81 Q79,101 79,99 V60 L52,87 Q50,89 48,87 L21,60 V99 Q21,101 19,101 H5 Q3,101 3,99 Z'
$svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-2 -2 104 106" fill="#FFFFFF"><path d="' + $upper + '"/><path d="' + $lower + '"/></svg>'
[IO.File]::WriteAllText((Join-Path $directory 'mark.svg'), $svg, [Text.UTF8Encoding]::new($false))
# Keep the outlined lettering and its named groups intact for the WPF renderer.
# One inherited ink also makes the exported SVG match the website's wordmark.
$wordmarkPath = Join-Path $directory 'wordmark.svg'
$wordmark = [Xml.XmlDocument]::new()
$wordmark.PreserveWhitespace = $true
$wordmark.Load($wordmarkPath)
foreach ($element in @($wordmark.SelectNodes('//*[@fill]'))) { $element.RemoveAttribute('fill') }
$wordmark.DocumentElement.SetAttribute('fill', '#FFFFFF')
$wordmark.SelectSingleNode('//*[@id="desc"]').InnerText = 'Monochrome VM symbol followed by the VineMix wordmark. Full-width i dots align with the capital V and M; both i stems are shortened from the top. All artwork is editable vector paths and shapes.'
[IO.File]::WriteAllText($wordmarkPath, $wordmark.OuterXml.Replace(' />','/>'), [Text.UTF8Encoding]::new($false))
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    # Match the in-app logo's normal ConsoleTextBrush soft white for Beta 6.
    # Leave the canvas and the gap between the two pieces transparent.
    # Use 80% of the canvas height: slightly larger, with clear outer margins.
    $scale = $size*.80/102.0
    $drawing.PushTransform([Windows.Media.TranslateTransform]::new(($size-100*$scale)/2,($size-102*$scale)/2))
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($scale,$scale))
    $ink = [Windows.Media.BrushConverter]::new().ConvertFromString('#E8EBF2')
    $drawing.DrawGeometry($ink,$null,[Windows.Media.Geometry]::Parse($upper))
    $drawing.DrawGeometry($ink,$null,[Windows.Media.Geometry]::Parse($lower))
    $drawing.Pop(); $drawing.Pop(); $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new(); $encoder.Save($stream)
    $frames += ,@($size,$stream.ToArray()); $stream.Dispose()
}
foreach ($name in @('app.ico','tray.ico')) {
    $stream = [IO.File]::Create((Join-Path $directory $name))
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16*$frames.Count
    foreach ($frame in $frames) {
        $side = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
        $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
        $offset += $frame[1].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
    $writer.Dispose()
}
[IO.File]::WriteAllBytes((Join-Path $directory 'app.png'), $frames[-1][1])
Write-Output 'Generated monochrome VM SVG artwork and nine-resolution application/tray icons.'
