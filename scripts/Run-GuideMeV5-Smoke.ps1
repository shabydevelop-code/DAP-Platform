param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('web', 'windows', 'mixed')]
    [string]$Target,

    [Parameter(Mandatory = $true)]
    [string]$DatabasePath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\GuideMe.App\GuideMe.App.csproj'
$db = [System.IO.Path]::GetFullPath($DatabasePath)

if (-not (Test-Path -LiteralPath $db -PathType Leaf)) {
    throw "V5 database file does not exist: $db"
}

$production = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'GuideMe\Data\GuideMe-V5.db'
if ([string]::Equals($db, [System.IO.Path]::GetFullPath($production), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Smoke testing requires a separate V5 database copy; do not use the active GuideMe-V5.db.'
}

$guide = switch ($Target) {
    'web' { 'sampleapp-web-guide' }
    'windows' { 'sampleapp-windows-guide' }
    'mixed' { 'sampleapp-mixed-smoke-guide' }
}

Push-Location $root
try {
    Write-Host "Validating V5 plan for $guide ..."
    & dotnet run --no-build -c Release --project $project -- --validate-v5 $db --guide $guide
    if ($LASTEXITCODE -ne 0) {
        $report = Join-Path ([System.IO.Path]::GetTempPath()) 'GuideMe-V5-validation-error.txt'
        if (Test-Path -LiteralPath $report) {
            Get-Content -LiteralPath $report | Out-Host
        }
        throw "Cannot start V5 learner: compilation failed (exit code $LASTEXITCODE)."
    }

    Write-Host "Starting isolated V5 learner: $guide"
    Write-Host 'Start the corresponding SampleApp demo launcher separately before running this script.'
    $runStartedUtc = [DateTime]::UtcNow
    & dotnet run --no-build -c Release --project $project -- --guide $guide --v5-db $db
    if ($LASTEXITCODE -ne 0) {
        $learnerExitCode = $LASTEXITCODE
        $logDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GuideMe\Logs'
        $latestError = Get-ChildItem -LiteralPath $logDir -Filter 'session-error-*.log' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $runStartedUtc.AddSeconds(-2) } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -ne $latestError) {
            Write-Host "V5 learner failure log: $($latestError.FullName)"
            Get-Content -LiteralPath $latestError.FullName | Out-Host
        }
        else {
            Write-Warning "No new GuideMe session-error log found in $logDir. Check for a popup, application startup failure, or non-exception exit."
        }
        throw "V5 learner finished with exit code $learnerExitCode."
    }
}
finally {
    Pop-Location
}
