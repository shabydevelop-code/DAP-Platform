param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDatabase
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [System.IO.Path]::GetFullPath($SourceDatabase)
$target = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'GuideMe\Data\GuideMe-V5.db'
$legacy = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'GuideMe\Data\GuideMe.db'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Source V5 database does not exist: $source" }
if ([string]::Equals($source, $legacy, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to provision V5 from the legacy production database.'
}
if ([string]::Equals($source, $target, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and target paths are identical.'
}
if (Test-Path -LiteralPath $target) {
    throw "Destination exists; refusing to overwrite: $target"
}

$validator = Join-Path $PSScriptRoot 'Validate-GuideMeV5.ps1'
& $validator -DatabasePath $source
if ($LASTEXITCODE -ne 0) { throw 'V5 validation failed; no file was copied.' }
$destinationDir = Split-Path -Parent $target
New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
Write-Host "V5 database provisioned: $target"
Write-Host "Legacy database untouched: $legacy"
