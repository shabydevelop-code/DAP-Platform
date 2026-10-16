param(
    [switch]$FailOnProductionReferences
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tokens = @(
    'SqliteGuideStepRepository',
    'SqliteDatabaseInitializer',
    'IGuideStepRepository',
    'SqliteDatabaseOptions.CreateDefault',
    'SqliteConnectionFactory',
    'SaveStepAsync',
    'ReplaceApplicationContextsAsync'
)

$areas = @('src', 'demos', 'tests', 'scripts')
$files = foreach ($area in $areas) {
    $dir = Join-Path $root $area
    if (Test-Path -LiteralPath $dir) {
        Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction Stop |
            Where-Object {
                $_.Extension -in @('.cs', '.csproj', '.ps1', '.props', '.targets') -and
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
                $_.Name -ne 'Audit-GuideMeLegacy.ps1'
            }
    }
}

$results = foreach ($file in $files) {
    $rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $relativePath = if ($file.FullName.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $file.FullName.Substring($rootPrefix.Length)
    } else {
        throw "File is outside repository root: $($file.FullName)"
    }
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        $lineNumber++
        foreach ($token in $tokens) {
            if ($line.IndexOf($token, [System.StringComparison]::Ordinal) -ge 0) {
                $category = if ($relativePath -match '^(src)[\\/]') { 'ProductionSource' }
                    elseif ($relativePath -match '^(tests)[\\/]') { 'Tests' }
                    elseif ($relativePath -match '^(demos)[\\/]') { 'Demos' }
                    else { 'Scripts' }
                [PSCustomObject]@{
                    Category = $category
                    File = $relativePath
                    Line = $lineNumber
                    Symbol = $token
                    Source = $line.Trim()
                }
            }
        }
    }
}
$results = @($results | Sort-Object Category, File, Line, Symbol)
$output = Join-Path $root 'docs\legacy-dependency-audit.csv'
$results | Export-Csv -LiteralPath $output -NoTypeInformation -Encoding UTF8
Write-Host "Legacy dependency audit: $($results.Count) references"
Write-Host "Report: $output"
$results | Group-Object Category, Symbol | Sort-Object Name |
    Select-Object Name, Count | Format-Table -AutoSize | Out-Host

if ($FailOnProductionReferences -and @($results | Where-Object Category -eq 'ProductionSource').Count -gt 0) {
    throw 'Legacy symbols remain in production source. See the generated CSV.'
}
