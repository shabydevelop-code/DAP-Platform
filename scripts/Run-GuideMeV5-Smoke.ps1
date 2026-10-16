param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('web', 'windows', 'mixed')]
    [string]$Target,

    [Parameter(Mandatory = $true)]
    [string]$DatabasePath,

    [ValidateSet('manual', 'hybrid')]
    [string]$Mode = 'manual'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\DAP.App\DAP.App.csproj'
$db = [System.IO.Path]::GetFullPath($DatabasePath)

if (-not (Test-Path -LiteralPath $db -PathType Leaf)) {
    throw "V5 database file does not exist: $db"
}

$production = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'GuideMe\Data\GuideMe.db'
if ([string]::Equals($db, [System.IO.Path]::GetFullPath($production), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to run V5 against the production GuideMe.db. Use an isolated copy.'
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

    Write-Host "Starting isolated V5 learner: $guide (mode: $Mode)"
    Write-Host 'Start the corresponding SampleApp demo launcher separately before running this script.'
    & dotnet run --no-build -c Release --project $project -- --guide $guide --mode $Mode --v5-db $db
    if ($LASTEXITCODE -ne 0) {
        throw "V5 learner finished with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
