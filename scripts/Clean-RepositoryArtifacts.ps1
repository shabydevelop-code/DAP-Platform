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
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target.FullName)) { continue }
    Write-Host "Removing $($target.FullName)"
    Remove-Item -LiteralPath $target.FullName -Recurse -Force
    $removed++
}

$after = Get-DirectorySizeBytes $rootPath
$freed = [Math]::Max(0L, $before - $after)

Write-Host ""
Write-Host "Removed:    $removed directories"
Write-Host "After:      $(Format-Size $after)"
Write-Host "Freed:      $(Format-Size $freed)"
Write-Host ""
Write-Host "Only regenerable repository build artifacts were removed. NuGet's global package cache is outside this repository and is intentionally untouched."
