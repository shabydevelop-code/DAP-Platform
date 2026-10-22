param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Add-Type -AssemblyName System.Drawing

# The Instructor needs a distinct silhouette at Chrome's 16px toolbar size.
# A solid blue disc + a large white/gold pencil distinguish it from Learner's
# thin monochrome bulb. All shapes are vector drawn at a normalized 128px size.
foreach ($size in @(16, 48, 128)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $blue = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 29, 99, 200))
    $gold = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 191, 55), 19)
    $white = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 10)
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 14, 60, 131), 27)
    $tip = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.ScaleTransform($size / 128.0, $size / 128.0)
        # A large solid disk is clearly identifiable even at 16x16.
        $g.FillEllipse($blue, 3, 3, 122, 122)

        # Lightbulb identity, deliberately secondary to the pencil.
        $g.DrawEllipse($white, 28, 19, 70, 71)
        $g.DrawLine($white, 44, 93, 81, 93)
        $g.DrawLine($white, 47, 105, 76, 105)

        # Oversized diagonal pencil; outline keeps it legible over the bulb.
        $g.DrawLine($outline, 48, 109, 107, 46)
        $g.DrawLine($gold, 48, 109, 107, 46)
        $g.FillPolygon($tip, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(40, 118),
            [System.Drawing.PointF]::new(45, 94),
            [System.Drawing.PointF]::new(62, 110)
        ))
        $g.DrawLine($white, 104, 43, 111, 50)
        $path = Join-Path $Destination "icon$size.png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Instructor icon generation failed: $path"
        }
    }
    finally {
        $g.Dispose()
        $bitmap.Dispose()
        $blue.Dispose()
        $gold.Dispose()
        $white.Dispose()
        $outline.Dispose()
        $tip.Dispose()
    }
}
Write-Host 'PASS: distinct Instructor toolbar icons generated (16, 48, 128).'
