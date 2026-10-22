param(
    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [string]$SourceImage = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\GuideMe.Instructor.ChromeExtension\instructor-source.png')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $SourceImage)) {
    throw "Original Instructor image is missing: $SourceImage. Place the user's supplied instructor-source.png in the Instructor extension source folder. No substitute icon will be generated."
}
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$source = [System.Drawing.Image]::FromFile($SourceImage)
try {
    if ($source.Width -ne $source.Height) {
        throw 'The original Instructor icon must be square.'
    }
    foreach ($size in @(16, 48, 128)) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $path = Join-Path $Destination "icon$size.png"
            $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally { $source.Dispose() }
Write-Host 'PASS: Instructor icons resized from the exact user-supplied original (16, 48, 128); no new artwork.'
