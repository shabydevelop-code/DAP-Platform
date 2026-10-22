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
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $gold = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 235, 169, 41))
    $navy = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 31, 47, 67))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($base, 0, 0, $size, $size)

        # Design on a normalized 128x128 canvas, so all icon sizes share one form.
        $graphics.ScaleTransform($size / 128.0, $size / 128.0)
        # A white silhouette preserves contrast on light and dark Chrome toolbars.
        $outline = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(67, 106)),
            (New-Object System.Drawing.PointF(72, 81)),
            (New-Object System.Drawing.PointF(99, 50)),
            (New-Object System.Drawing.PointF(114, 63)),
            (New-Object System.Drawing.PointF(88, 94)),
            (New-Object System.Drawing.PointF(67, 106))
        )
        $graphics.FillPolygon($white, $outline)
        $body = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(77, 83)),
            (New-Object System.Drawing.PointF(99, 57)),
            (New-Object System.Drawing.PointF(109, 66)),
            (New-Object System.Drawing.PointF(86, 92))
        )
        $graphics.FillPolygon($gold, $body)
        $tip = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(75, 86)),
            (New-Object System.Drawing.PointF(86, 95)),
            (New-Object System.Drawing.PointF(69, 103))
        )
        $graphics.FillPolygon($white, $tip)
        $lead = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(69, 103)),
            (New-Object System.Drawing.PointF(73, 94)),
            (New-Object System.Drawing.PointF(79, 100))
        )
        $graphics.FillPolygon($navy, $lead)
        $graphics.FillPolygon($navy, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(101, 54)),
            (New-Object System.Drawing.PointF(107, 47)),
            (New-Object System.Drawing.PointF(119, 58)),
            (New-Object System.Drawing.PointF(113, 65))
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
