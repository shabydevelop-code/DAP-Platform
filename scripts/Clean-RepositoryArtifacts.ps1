param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [switch]$IncludeVs
)

$ErrorActionPreference = "Stop"

function Get-DirectorySizeBytes([string]$Path) {
    $sum = 0L
    Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        ForEach-Object { $sum += $_.Length }
    return $sum
}

function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N1} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N1} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

$rootPath = (Resolve-Path $Root).Path
$before = Get-DirectorySizeBytes $rootPath

Write-Host "Repository: $rootPath"
Write-Host "Before:     $(Format-Size $before)"

$targets = Get-ChildItem -LiteralPath $rootPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @("bin", "obj") -or
        ($IncludeVs -and $_.Name -eq ".vs")
    } |
    Sort-Object { $_.FullName.Length } -Descending

$removed = 0
$skipped = @()
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target.FullName)) { continue }
    Write-Host "Removing $($target.FullName)"
    try {
        Remove-Item -LiteralPath $target.FullName -Recurse -Force -ErrorAction Stop
        $removed++
    }
    catch [System.UnauthorizedAccessException] {
        $skipped += $target.FullName
        Write-Warning "Skipped locked/in-use directory: $($target.FullName)"
    }
    catch [System.IO.IOException] {
        $skipped += $target.FullName
        Write-Warning "Skipped locked/in-use directory: $($target.FullName)"
    }
}

$after = Get-DirectorySizeBytes $rootPath
$freed = [Math]::Max(0L, $before - $after)

Write-Host ""
Write-Host "Removed:    $removed directories"
Write-Host "Skipped:    $($skipped.Count) locked/in-use directories"
Write-Host "After:      $(Format-Size $after)"
Write-Host "Freed:      $(Format-Size $freed)"

if ($skipped.Count -gt 0) {
    Write-Host ""
    Write-Host "Locked/in-use directories:"
    $skipped | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "Close/stop the process using those build outputs and run this script again to remove the remainder."
}

Write-Host ""
Write-Host "Only regenerable repository build artifacts were removed. NuGet's global package cache is outside this repository and is intentionally untouched."
