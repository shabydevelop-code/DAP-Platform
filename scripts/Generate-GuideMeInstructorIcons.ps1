param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$learnerIcons = Join-Path $root 'src\GuideMe.Learner.ChromeExtension'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Add-Type -AssemblyName System.Drawing

foreach ($size in @(16, 48, 128)) {
    $inputPath = Join-Path $learnerIcons "icon$size.png"
    if (-not (Test-Path -LiteralPath $inputPath)) {
        throw "GuideMe Learner base icon is missing: $inputPath"
    }

    $base = [System.Drawing.Image]::FromFile($inputPath)
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $gold = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 235, 169, 41))
    $navy = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 31, 47, 67))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($base, 0, 0, $size, $size)

        # Design on a normalized 128x128 canvas, so all icon sizes share one form.
        $graphics.ScaleTransform($size / 128.0, $size / 128.0)
        # A white silhouette preserves contrast on light and dark Chrome toolbars.
        $outline = [System.Drawing.PointF[]]@(
            ([System.Drawing.PointF]::new(67, 106)),
            ([System.Drawing.PointF]::new(72, 81)),
            ([System.Drawing.PointF]::new(99, 50)),
            ([System.Drawing.PointF]::new(114, 63)),
            ([System.Drawing.PointF]::new(88, 94)),
            ([System.Drawing.PointF]::new(67, 106))
        )
        $graphics.FillPolygon($white, $outline)
        $body = [System.Drawing.PointF[]]@(
            ([System.Drawing.PointF]::new(77, 83)),
            ([System.Drawing.PointF]::new(99, 57)),
            ([System.Drawing.PointF]::new(109, 66)),
            ([System.Drawing.PointF]::new(86, 92))
        )
        $graphics.FillPolygon($gold, $body)
        $tip = [System.Drawing.PointF[]]@(
            ([System.Drawing.PointF]::new(75, 86)),
            ([System.Drawing.PointF]::new(86, 95)),
            ([System.Drawing.PointF]::new(69, 103))
        )
        $graphics.FillPolygon($white, $tip)
        $lead = [System.Drawing.PointF[]]@(
            ([System.Drawing.PointF]::new(69, 103)),
            ([System.Drawing.PointF]::new(73, 94)),
            ([System.Drawing.PointF]::new(79, 100))
        )
        $graphics.FillPolygon($navy, $lead)
        $graphics.FillPolygon($navy, [System.Drawing.PointF[]]@(
            ([System.Drawing.PointF]::new(101, 54)),
            ([System.Drawing.PointF]::new(107, 47)),
            ([System.Drawing.PointF]::new(119, 58)),
            ([System.Drawing.PointF]::new(113, 65))
        ))
        $path = Join-Path $Destination "icon$size.png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        if (-not (Test-Path -LiteralPath $path)) { throw "Icon generation failed: $path" }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $base.Dispose()
        $gold.Dispose()
        $navy.Dispose()
        $white.Dispose()
    }
}
Write-Host 'PASS: GuideMe Instructor icons generated (16, 48, 128).'
