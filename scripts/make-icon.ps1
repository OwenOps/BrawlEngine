param(
    [string]$SourcePng = "D:\AProg\BrawlEngine\host\Assets\icon-512.png",
    [string]$OutIco = "D:\AProg\BrawlEngine\host\Assets\app.ico"
)

# Desktop shortcuts ignore PNG alpha and fill a white 48x48, then punch holes
# with the 1-bit AND mask. favicon.io writes AND=0, which leaves the white box.
# Transparent pixels must be RGB 0 and AND bit 1.

Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $OutIco
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$source = [System.Drawing.Bitmap]::FromFile($SourcePng)
Write-Output ("Source size: {0}x{1} format={2}" -f $source.Width, $source.Height, $source.PixelFormat)

function New-ScaledArgbBitmap([System.Drawing.Image]$src, [int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution($src.HorizontalResolution, $src.VerticalResolution)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $graphics.Dispose()
    return $bitmap
}

function Get-Dib32IconImage([System.Drawing.Bitmap]$bitmap) {
    $width = $bitmap.Width
    $height = $bitmap.Height
    $xorStride = $width * 4
    $xor = New-Object byte[] ($xorStride * $height)
    $andStride = [int][Math]::Ceiling($width / 32.0) * 4
    $and = New-Object byte[] ($andStride * $height)

    for ($y = 0; $y -lt $height; $y++) {
        $sourceY = $height - 1 - $y
        for ($x = 0; $x -lt $width; $x++) {
            $pixel = $bitmap.GetPixel($x, $sourceY)
            $index = ($y * $xorStride) + ($x * 4)
            if ($pixel.A -lt 16) {
                $xor[$index] = 0
                $xor[$index + 1] = 0
                $xor[$index + 2] = 0
                $xor[$index + 3] = 0
                $bit = 7 - ($x % 8)
                $andByte = ($y * $andStride) + [int][Math]::Floor($x / 8)
                $and[$andByte] = $and[$andByte] -bor (1 -shl $bit)
            }
            else {
                $xor[$index] = $pixel.B
                $xor[$index + 1] = $pixel.G
                $xor[$index + 2] = $pixel.R
                $xor[$index + 3] = $pixel.A
            }
        }
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([UInt32]40)
    $writer.Write([Int32]$width)
    $writer.Write([Int32]($height * 2))
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]($xor.Length + $and.Length))
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)
    $writer.Write($xor)
    $writer.Write($and)
    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return $bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[byte[]]
foreach ($size in $sizes) {
    $scaled = New-ScaledArgbBitmap $source $size
    $images.Add((Get-Dib32IconImage $scaled))
    $scaled.Dispose()
}

$source.Dispose()

$fileStream = New-Object System.IO.FileStream $OutIco, ([System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($fileStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$headerSize = 6
$entrySize = 16
$dataOffset = $headerSize + ($entrySize * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dimensionByte = if ($size -ge 256) { 0 } else { $size }
    $bytes = $images[$i]
    $writer.Write([byte]$dimensionByte)
    $writer.Write([byte]$dimensionByte)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$dataOffset)
    $dataOffset += $bytes.Length
}

foreach ($bytes in $images) {
    $writer.Write($bytes)
}

$writer.Flush()
$writer.Dispose()
$fileStream.Dispose()

Write-Output ("Wrote BMP-32 ICO with AND mask {0}" -f $OutIco)
