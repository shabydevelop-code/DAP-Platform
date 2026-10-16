param(
    [string]$ActiveDirectory = 'C:\GuideMe',
    [string]$StagingDirectory = 'C:\GuideMe-V5-Staging',
    [string]$V5Database = 'C:\ProgramData\GuideMe\Data\GuideMe-V5.db',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

function Assert-File([string]$FilePath) {
    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "Required file missing: $FilePath"
    }
}

$active = [IO.Path]::GetFullPath($ActiveDirectory).TrimEnd(
    [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$staging = [IO.Path]::GetFullPath($StagingDirectory).TrimEnd(
    [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if ([string]::Equals($active, $staging, [StringComparison]::OrdinalIgnoreCase) -or
    $active.StartsWith($staging + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $staging.StartsWith($active + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Active and staging directories must be separate.'
}
if (-not (Test-Path -LiteralPath $active -PathType Container)) {
    throw "Active installation missing: $active"
}
if (-not (Test-Path -LiteralPath $staging -PathType Container)) {
    throw "Staging installation missing: $staging"
}
Assert-File (Join-Path $staging 'DAP.exe')
Assert-File (Join-Path $staging 'NativeHost\GuideMe.NativeHost.exe')
Assert-File (Join-Path $active 'DAP.exe')
Assert-File $V5Database

$running = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('DAP.exe', 'GuideMe.NativeHost.exe') -and
    $_.ExecutablePath -and
    ($_.ExecutablePath.StartsWith($active + '\', [StringComparison]::OrdinalIgnoreCase) -or
     $_.ExecutablePath.StartsWith($staging + '\', [StringComparison]::OrdinalIgnoreCase))
})
if ($running.Count -gt 0) {
    $running | Select-Object Name, ProcessId, ExecutablePath | Format-Table -AutoSize
    throw 'Close all GuideMe and Native Host processes before cutover.'
}

# Keep the installed Chrome extension and legacy database untouched.
# The legacy root-level NativeHost EXE in staging was produced by an earlier
# publish layout; do not copy it into the active installation.
$appItems = @(Get-ChildItem -LiteralPath $staging -Force | Where-Object {
    $_.Name -notin @('ChromeExtension', 'NativeHost', 'GuideMe.NativeHost.exe')
})
$nativeSource = Join-Path $staging 'NativeHost'
$nativeDestination = Join-Path $active 'NativeHost'
if (-not (Test-Path -LiteralPath $nativeSource -PathType Container)) {
    throw "Staged NativeHost directory missing: $nativeSource"
}

Write-Host "Source: $staging"
Write-Host "Destination: $active"
Write-Host "Application root entries: $($appItems.Count)"
Write-Host "Native Host source: $nativeSource"
Write-Host 'Excluded: ChromeExtension, legacy database, staging root-level NativeHost executable.'
if (-not $Apply) {
    Write-Host 'PREVIEW ONLY. Re-run with -Apply to perform the replacement.'
    return
}

# Copy application files without deleting user/configuration files.
foreach ($item in $appItems) {
    Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $active $item.Name) -Recurse -Force -ErrorAction Stop
}
New-Item -ItemType Directory -Path $nativeDestination -Force | Out-Null
foreach ($item in @(Get-ChildItem -LiteralPath $nativeSource -Force)) {
    Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $nativeDestination $item.Name) -Recurse -Force -ErrorAction Stop
}

foreach ($relative in @('DAP.exe', 'NativeHost\GuideMe.NativeHost.exe')) {
    $source = Join-Path $staging $relative
    $target = Join-Path $active $relative
    Assert-File $target
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw "Verification failed for $relative"
    }
}
Write-Host 'PASS: GuideMe V5 binaries installed; EXE hashes verified.'
Write-Host 'Databases and installed Chrome extension were not changed.'
