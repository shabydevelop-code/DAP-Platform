$ErrorActionPreference = "Stop"
$env:DAP_DIAGNOSTICS_ROOT = $PSScriptRoot

$web = Join-Path $PSScriptRoot "Runners\Web\DAP.TestCRM.Web.E2E.exe"
$windows = Join-Path $PSScriptRoot "Runners\Windows\DAP.TestCRM.Windows.E2E.exe"

& $web --reset-guide
if ($LASTEXITCODE -ne 0) { throw "Web Guide initialization failed." }

& $windows --reset-guide
if ($LASTEXITCODE -ne 0) { throw "Windows Guide initialization failed." }

Write-Host "DAP diagnostic Guides initialized successfully."
