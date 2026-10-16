param(
    [Parameter(Mandatory = $true)]
    [string]$DatabasePath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fullDatabasePath = [System.IO.Path]::GetFullPath($DatabasePath)
if (-not (Test-Path -LiteralPath $fullDatabasePath -PathType Leaf)) {
    throw "V5 database not found: $fullDatabasePath"
}

$project = Join-Path $repoRoot 'src\GuideMe.App\GuideMe.App.csproj'
$guides = @(
    'sampleapp-web-guide',
    'sampleapp-windows-guide',
    'sampleapp-mixed-smoke-guide'
)

Push-Location $repoRoot
try {
    foreach ($guide in $guides) {
        Write-Host "Validating V5 guide: $guide"
        & dotnet run --no-build -c Release --project $project -- --validate-v5 $fullDatabasePath --guide $guide
        if ($LASTEXITCODE -ne 0) {
            $report = Join-Path ([System.IO.Path]::GetTempPath()) "GuideMe-V5-validation-error.txt"
            if (Test-Path -LiteralPath $report) {
                Write-Host "V5 diagnostic report:"
                Get-Content -LiteralPath $report | Out-Host
            }
            throw "V5 validation failed: $guide (exit code $LASTEXITCODE)"
        }
    }
    Write-Host "All 3 V5 guides passed compilation validation."
}
finally {
    Pop-Location
}
