param(
    [string]$SourcePng = "D:\AProg\BrawlEngine\ui\public\logo.png",
    [string]$OutIco = "D:\AProg\BrawlEngine\host\Assets\app.ico"
)

Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $OutIco
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$source = [System.Drawing.Image]::FromFile($SourcePng)
Write-Output ("Source size: {0}x{1}" -f $source.Width, $source.Height)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBytesBySize = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($source, 0, 0, $size, $size)
    $graphics.Dispose()

    $memoryStream = New-Object System.IO.MemoryStream
    $bitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesBySize.Add($memoryStream.ToArray())
    $memoryStream.Dispose()
    $bitmap.Dispose()
}

$source.Dispose()

$fileStream = New-Object System.IO.FileStream $OutIco, ([System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter $fileStream

# ICONDIR
$writer.Write([UInt16]0)               # Reserved
$writer.Write([UInt16]1)               # Type: 1 = icon
$writer.Write([UInt16]$sizes.Count)    # Image count

$headerSize = 6
$entrySize = 16
$dataOffset = $headerSize + ($entrySize * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dimensionByte = if ($size -ge 256) { 0 } else { $size }
    $bytes = $pngBytesBySize[$i]

    $writer.Write([byte]$dimensionByte)  # Width
    $writer.Write([byte]$dimensionByte)  # Height
    $writer.Write([byte]0)               # Color palette
    $writer.Write([byte]0)               # Reserved
    $writer.Write([UInt16]1)             # Color planes
    $writer.Write([UInt16]32)            # Bits per pixel
    $writer.Write([UInt32]$bytes.Length) # Size of image data
    $writer.Write([UInt32]$dataOffset)   # Offset of image data

    $dataOffset += $bytes.Length
}

foreach ($bytes in $pngBytesBySize) {
    $writer.Write($bytes)
}

$writer.Flush()
$writer.Dispose()
$fileStream.Dispose()

Write-Output ("Wrote {0}" -f $OutIco)
