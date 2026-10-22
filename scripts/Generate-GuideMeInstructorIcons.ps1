param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$learnerIcons = Join-Path $root 'src\GuideMe.Learner.ChromeExtension'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Add-Type -AssemblyName System.Drawing

# Keep the Learner lamp artwork itself, pixel-for-pixel, as the Instructor
# foundation. Only draw a large, clearly legible pencil to its lower-right.
foreach ($size in @(16, 48, 128)) {
    $sourcePath = Join-Path $learnerIcons "icon$size.png"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing original GuideMe lamp: $sourcePath"
    }
    $base = [System.Drawing.Image]::FromFile($sourcePath)
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $halo = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 5)
    $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 30, 38, 48), 3)
    $gold = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 235, 168, 42))
    $tip = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $lead = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 30, 38, 48))
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.Clear([System.Drawing.Color]::Transparent)
        # This is the unchanged production Learner bulb: no recoloring or redrawing.
        $g.DrawImage($base, 0, 0, $size, $size)
        $g.ScaleTransform($size / 128.0, $size / 128.0)

        # The pencil remains large enough to be readable at 16x16, but has a
        # separate silhouette next to the bulb instead of covering its centre.
        $body = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(98, 42),
            [System.Drawing.PointF]::new(118, 52),
            [System.Drawing.PointF]::new(96, 97),
            [System.Drawing.PointF]::new(76, 87)
        )
        $g.DrawPolygon($halo, $body)
        $g.FillPolygon($gold, $body)
        $g.DrawPolygon($border, $body)

        $point = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(76, 87),
            [System.Drawing.PointF]::new(96, 97),
            [System.Drawing.PointF]::new(70, 111)
        )
        $g.DrawPolygon($halo, $point)
        $g.FillPolygon($tip, $point)
        $g.DrawPolygon($border, $point)

        $leadPoint = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(70, 111),
            [System.Drawing.PointF]::new(77, 101),
            [System.Drawing.PointF]::new(81, 105)
        )
        $g.FillPolygon($lead, $leadPoint)
        $g.DrawLine($border, 98, 42, 118, 52)

        $destinationPath = Join-Path $Destination "icon$size.png"
        $bitmap.Save($destinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
        if (-not (Test-Path -LiteralPath $destinationPath) -or
            (Get-Item -LiteralPath $destinationPath).Length -eq 0) {
            throw "Instructor icon generation failed: $destinationPath"
        }
    }
    finally {
        $g.Dispose()
        $bitmap.Dispose()
        $base.Dispose()
        $halo.Dispose()
        $border.Dispose()
        $gold.Dispose()
        $tip.Dispose()
        $lead.Dispose()
    }
}
Write-Host 'PASS: original GuideMe lamp + separate pencil generated for Instructor (16, 48, 128).'
