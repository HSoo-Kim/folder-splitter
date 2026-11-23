param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng,
    # Optional artwork for the 32 px and smaller frames, cropped tighter so the icon stays legible there.
    [string]$SmallSourcePng
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot "../src/FolderSplitter/Assets"
[IO.Directory]::CreateDirectory($destination) | Out-Null
$sourcePath = (Resolve-Path $SourcePng).Path
$smallPath = if ($SmallSourcePng) { (Resolve-Path $SmallSourcePng).Path } else { $sourcePath }
$source = [Drawing.Bitmap]::new($sourcePath)
$small = [Drawing.Bitmap]::new($smallPath)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()

try {
    foreach ($image in @($source, $small)) {
        if ($image.GetPixel(0, 0).A -ne 0) { throw "The icon source must have a transparent background." }
    }
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
            $graphics.DrawImage($(if ($size -le 32) { $small } else { $source }), [Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($memory.ToArray())
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
    $small.Dispose()
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
$copies = @(, @($sourcePath, "app.png"))
if ($SmallSourcePng) { $copies += , @($smallPath, "app-small.png") }
foreach ($pair in $copies) {
    $pngPath = [IO.Path]::GetFullPath((Join-Path $destination $pair[1]))
    if ($pair[0] -ne $pngPath) { Copy-Item -LiteralPath $pair[0] -Destination $pngPath -Force }
}
Write-Output "Icon created with transparent 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel frames; 32 px and smaller use the small artwork."
