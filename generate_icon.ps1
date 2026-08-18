Add-Type -AssemblyName PresentationCore, WindowsBase

$sourcePath = "C:\Users\silve\.gemini\antigravity\brain\919c504f-8051-47bb-9cca-97ad52fea379\.user_uploaded\media_1787027704088.png"
$outputPath = "C:\Users\silve\.gemini\antigravity\scratch\leeyes-viewer\app.ico"

# Load original PNG preserving 32-bit ARGB / Alpha transparency
$uri = New-Object System.Uri($sourcePath, [System.UriKind]::Absolute)
$decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create($uri, [System.Windows.Media.Imaging.BitmapCreateOptions]::None, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$origFrame = $decoder.Frames[0]

$sizes = @(256, 128, 64, 48, 32, 16)
$imagesData = @()

foreach ($size in $sizes) {
    # Scale bitmap to desired size preserving transparency
    $group = New-Object System.Windows.Media.DrawingGroup
    $rect = New-Object System.Windows.Rect(0, 0, $size, $size)
    $imageDrawing = New-Object System.Windows.Media.ImageDrawing($origFrame, $rect)
    $group.Children.Add($imageDrawing) | Out-Null
    
    $drawingVisual = New-Object System.Windows.Media.DrawingVisual
    $drawingContext = $drawingVisual.RenderOpen()
    $drawingContext.DrawDrawing($group)
    $drawingContext.Close()

    # Pbgra32 preserves full alpha channel transparency
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($drawingVisual)

    # Encode as PNG bytes
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    $pngBytes = $ms.ToArray()
    $ms.Close()

    $imagesData += @{
        Size = $size
        Bytes = $pngBytes
    }
}

# Write ICO Binary Structure
$fs = New-Object System.IO.FileStream($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONHEADER (6 bytes)
$bw.Write([UInt16]0) # Reserved
$bw.Write([UInt16]1) # Type (1 = ICO)
$bw.Write([UInt16]$sizes.Length) # Image count

# Calculate initial offset for image data (6 + 16 * count)
$offset = 6 + (16 * $sizes.Length)

# ICONDIRENTRY (16 bytes each)
foreach ($img in $imagesData) {
    $w = if ($img.Size -ge 256) { 0 } else { [byte]$img.Size }
    $h = if ($img.Size -ge 256) { 0 } else { [byte]$img.Size }
    $bw.Write([byte]$w)            # Width
    $bw.Write([byte]$h)            # Height
    $bw.Write([byte]0)             # Color count (0 for 32bpp)
    $bw.Write([byte]0)             # Reserved
    $bw.Write([UInt16]1)           # Color planes
    $bw.Write([UInt16]32)          # Bit count
    $bw.Write([UInt32]$img.Bytes.Length) # Bytes in resource
    $bw.Write([UInt32]$offset)     # Image offset

    $offset += $img.Bytes.Length
}

# Write Image Data Payloads
foreach ($img in $imagesData) {
    $bw.Write($img.Bytes)
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Successfully generated app.ico at $outputPath with $($sizes.Length) sizes (256, 128, 64, 48, 32, 16) preserving transparency."
