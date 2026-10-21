param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot "../src/QuadExplorer/Assets"
[IO.Directory]::CreateDirectory($destination) | Out-Null
$sourcePath = (Resolve-Path $SourcePng).Path
$source = [Drawing.Image]::FromFile($sourcePath)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()

try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $memory = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($memory.ToArray())
            if ($bitmap.GetPixel(0, 0).A -ne 0) {
                throw "The icon source must have a transparent background."
            }
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
            $memory.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$output = [IO.File]::Create((Join-Path $destination "app.ico"))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally {
    $writer.Dispose()
    $output.Dispose()
}
$pngPath = [IO.Path]::GetFullPath((Join-Path $destination "app.png"))
if ($sourcePath -ne $pngPath) {
    Copy-Item -LiteralPath $sourcePath -Destination $pngPath -Force
}
Write-Output "Icon created with transparent 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel frames."
