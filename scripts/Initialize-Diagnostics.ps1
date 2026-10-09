$ErrorActionPreference = "Stop"
$env:DAP_DIAGNOSTICS_ROOT = $PSScriptRoot

$runner = Join-Path $PSScriptRoot "Runners\Unified\DAP.E2E.exe"
if (!(Test-Path $runner)) { throw "Diagnostic runner was not found: $runner" }

& $runner --platform web --reset-guide
if ($LASTEXITCODE -ne 0) { throw "Web Guide initialization failed." }

& $runner --platform windows --reset-guide
if ($LASTEXITCODE -ne 0) { throw "Windows Guide initialization failed." }

Write-Host "DAP diagnostic Guides initialized successfully."
