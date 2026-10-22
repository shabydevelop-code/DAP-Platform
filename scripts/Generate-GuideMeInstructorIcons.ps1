param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$learnerIcons = Join-Path $root 'src\GuideMe.Learner.ChromeExtension'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Add-Type -AssemblyName System.Drawing

# Preserve the original Learner artwork and aspect ratio. It is proportionally
# scaled to the left to make physical room for an upright, full-height pencil.
# Both symbols occupy separate columns; the pencil never overlaps the bulb.
foreach ($size in @(16, 48, 128)) {
    $sourcePath = Join-Path $learnerIcons "icon$size.png"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing original GuideMe lamp: $sourcePath"
    }

    $base = [System.Drawing.Image]::FromFile($sourcePath)
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $yellow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 244, 182, 38))
    $dark = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 30, 35, 43), 3)
    $wood = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 250, 225, 175))
    $lead = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 30, 35, 43))
    $eraser = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 240, 151, 90))
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # 92x92 at (0, 18): source lightbulb is not redrawn or recolored.
        # Width and height scale equally so the original bulb keeps its shape.
        $lampSize = [int][Math]::Round($size * (92.0 / 128.0))
        $lampTop = [int][Math]::Round(($size - $lampSize) / 2)
        $g.DrawImage($base, 0, $lampTop, $lampSize, $lampSize)

        # Draw in a fixed coordinate system for 16, 48 and 128px.
        $g.ScaleTransform($size / 128.0, $size / 128.0)

        # Upright pencil: yellow body from y=24 to y=94; tip to y=113.
        # Its total height (94px) matches the visible height of the bulb.
        $g.FillRectangle($yellow, 99, 25, 17, 70)
        $g.DrawRectangle($dark, 99, 25, 17, 70)
        $g.FillRectangle($eraser, 99, 18, 17, 9)
        $g.DrawRectangle($dark, 99, 18, 17, 9)
        $tip = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(99, 95),
            [System.Drawing.PointF]::new(116, 95),
            [System.Drawing.PointF]::new(107.5, 115)
        )
        $g.FillPolygon($wood, $tip)
        $g.DrawPolygon($dark, $tip)
        $leadTip = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(103, 107),
            [System.Drawing.PointF]::new(112, 107),
            [System.Drawing.PointF]::new(107.5, 115)
        )
        $g.FillPolygon($lead, $leadTip)

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
        $yellow.Dispose()
        $dark.Dispose()
        $wood.Dispose()
        $lead.Dispose()
        $eraser.Dispose()
    }
}
Write-Host 'PASS: original GuideMe bulb with upright yellow pencil beside it (16, 48, 128).'
