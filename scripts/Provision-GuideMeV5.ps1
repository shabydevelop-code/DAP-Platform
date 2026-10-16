param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDatabase
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [System.IO.Path]::GetFullPath($SourceDatabase)
$target = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'GuideMe\Data\GuideMe-V5.db'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Source V5 database does not exist: $source" }
if ([string]::Equals($source, $target, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and target paths are identical.'
}
if (Test-Path -LiteralPath $target) {
    throw "Destination exists; refusing to overwrite: $target"
}

$validator = Join-Path $PSScriptRoot 'Validate-GuideMeV5.ps1'
# Validation is a PowerShell script that throws on any failed guide.
# Do not inspect $LASTEXITCODE here: it may be stale after a successful script.
& $validator -DatabasePath $source
$destinationDir = Split-Path -Parent $target
New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
Write-Host "V5 database provisioned: $target"
