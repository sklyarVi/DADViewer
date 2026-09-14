[CmdletBinding()]
param([string]$PreviewDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$svg = Get-Content -LiteralPath (Join-Path $repo 'assets/DADViewer.svg')
$sizes = @(16,20,24,32,48,64,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $dc.PushTransform([Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0))
    $rect = $svg.svg.rect
    $brush = [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($rect.fill)
    $dc.DrawRoundedRectangle($brush, $null, [Windows.Rect]::new([double]$rect.x, [double]$rect.y, [double]$rect.width, [double]$rect.height), [double]$rect.rx, [double]$rect.rx)
    foreach ($path in $svg.svg.path) {
        $brush = [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($path.stroke)
        $pen = [Windows.Media.Pen]::new($brush, [double]$path.'stroke-width')
        $pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
        $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        $dc.DrawGeometry($null, $pen, [Windows.Media.Geometry]::Parse($path.d))
    }
    $dc.Pop(); $dc.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new()
    $encoder.Save($memory)
    $bytes = $memory.ToArray(); $memory.Dispose(); $frames.Add($bytes)
    if ($PreviewDirectory) {
        New-Item -ItemType Directory -Force -Path $PreviewDirectory | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $PreviewDirectory "icon-$size.png"), $bytes)
    }
}
$output = [IO.File]::Create((Join-Path $repo 'assets/DADViewer.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($bytes in $frames) { $writer.Write($bytes) }
} finally { $writer.Dispose() }
Write-Output 'Generated assets/DADViewer.ico (16–256 px) from assets/DADViewer.svg.'
