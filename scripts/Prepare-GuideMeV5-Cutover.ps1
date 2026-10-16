param(
    [string]$ActiveDirectory = 'C:\GuideMe',
    [string]$StagingDirectory = 'C:\GuideMe-V5-Staging',
    [string]$LegacyDatabase = 'C:\ProgramData\GuideMe\Data\GuideMe.db',
    [string]$V5Database = 'C:\ProgramData\GuideMe\Data\GuideMe-V5.db',
    [string]$BackupRoot = 'C:\GuideMe-Backups'
)

$ErrorActionPreference = 'Stop'

function Assert-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file missing: $Path"
    }
}

function Assert-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required directory missing: $Path"
    }
}

# Preflight only: this command does not overwrite the installation or database.
Assert-Directory $ActiveDirectory
Assert-Directory $StagingDirectory
Assert-File (Join-Path $StagingDirectory 'DAP.exe')
Assert-File (Join-Path $StagingDirectory 'NativeHost\GuideMe.NativeHost.exe')
Assert-File (Join-Path $ActiveDirectory 'NativeHost\GuideMe.NativeHost.exe')
Assert-File (Join-Path $ActiveDirectory 'DAP.exe')
Assert-File $LegacyDatabase
Assert-File $V5Database

$active = [IO.Path]::GetFullPath($ActiveDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$staging = [IO.Path]::GetFullPath($StagingDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if ([string]::Equals($active, $staging, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Active and staging paths must differ.'
}

$running = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('DAP.exe', 'GuideMe.NativeHost.exe') -and
    $_.ExecutablePath -and
    ($_.ExecutablePath.StartsWith($active + '\', [StringComparison]::OrdinalIgnoreCase) -or
     $_.ExecutablePath.StartsWith($staging + '\', [StringComparison]::OrdinalIgnoreCase))
})
if ($running.Count -gt 0) {
    $running | Select-Object Name, ProcessId, ExecutablePath | Format-Table -AutoSize
    throw 'Close the active or staging GuideMe processes before backup.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $BackupRoot $stamp
if (Test-Path -LiteralPath $backup) { throw "Backup folder already exists: $backup" }
New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
$installationBackup = Join-Path $backup 'Installation'
Copy-Item -LiteralPath $ActiveDirectory -Destination $installationBackup -Recurse -Force -ErrorAction Stop

$dbBackup = Join-Path $backup 'GuideMe.db'
# File backup only; cannot guarantee a transactionally consistent snapshot if
# another process has the database open. Do not proceed to cutover until verified.
Copy-Item -LiteralPath $LegacyDatabase -Destination $dbBackup -ErrorAction Stop

$manifest = @(
    [pscustomobject]@{ Original = $LegacyDatabase; Backup = $dbBackup },
    [pscustomobject]@{ Original = (Join-Path $ActiveDirectory 'DAP.exe'); Backup = (Join-Path $installationBackup 'DAP.exe') },
    [pscustomobject]@{ Original = (Join-Path $ActiveDirectory 'NativeHost\GuideMe.NativeHost.exe'); Backup = (Join-Path $installationBackup 'NativeHost\GuideMe.NativeHost.exe') }
)
foreach ($item in $manifest) {
    Assert-File $item.Backup
    if ((Get-FileHash -LiteralPath $item.Original -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $item.Backup -Algorithm SHA256).Hash) {
        throw "Backup verification failed: $($item.Original)"
    }
}

Write-Host "PASS: Preflight and backups verified at $backup"
Write-Host 'Active installation and both databases were not modified.'
