param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("Web","Windows")]
    [string]$Platform,

    [Parameter(Mandatory=$true)]
    [ValidateSet("Manual","Hybrid")]
    [string]$Mode,

    [string]$GuideId
)

$ErrorActionPreference = "Stop"
$diagnosticsRoot = $PSScriptRoot
$productionRoot = Split-Path -Parent $diagnosticsRoot
$env:DAP_DIAGNOSTICS_ROOT = $diagnosticsRoot

$runner = Join-Path $diagnosticsRoot "Runners\Unified\DAP.E2E.exe"
if (!(Test-Path $runner)) { throw "Diagnostic runner was not found: $runner" }
if (!(Test-Path (Join-Path $productionRoot "DAP.exe"))) { throw "Production DAP.exe was not found: $productionRoot" }

$platformName = $Platform.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($GuideId)) {
    $GuideId = if ($platformName -eq "web") { "testcrm-web-canonical-workflow" } else { "testcrm-windows-canonical-workflow" }
}

# Reset the selected diagnostic Guide before the run, as in the legacy diagnostic script.
& $runner --platform $platformName --reset-guide
if ($LASTEXITCODE -ne 0) { throw "Diagnostic Guide initialization failed." }

$modeArg = if ($Mode -eq "Manual") { "--manual" } else { "--hybrid" }
& $runner --platform $platformName --guide $GuideId $modeArg --published-dap $productionRoot
exit $LASTEXITCODE
