param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("Web","Windows")]
    [string]$Platform,

    [Parameter(Mandatory=$true)]
    [ValidateSet("Fast","Visual","Manual","Unguided","ManualFromStep","VisualFromStep")]
    [string]$Mode,

    [int]$Step = 1
)

$ErrorActionPreference = "Stop"
$diagnosticsRoot = $PSScriptRoot
$productionRoot = Split-Path -Parent $diagnosticsRoot
$env:DAP_DIAGNOSTICS_ROOT = $diagnosticsRoot

if ($Platform -eq "Web") {
    $runner = Join-Path $diagnosticsRoot "Runners\Web\DAP.TestCRM.Web.E2E.exe"
} else {
    $runner = Join-Path $diagnosticsRoot "Runners\Windows\DAP.TestCRM.Windows.E2E.exe"
}

if (!(Test-Path $runner)) { throw "Diagnostic runner was not found: $runner" }
if (!(Test-Path (Join-Path $productionRoot "DAP.exe"))) { throw "Production DAP.exe was not found: $productionRoot" }

# Keep the packaged diagnostic Guide definition canonical on every customer run.
# This resets only the dedicated TestCRM Guide for the selected platform.
& $runner --reset-guide
if ($LASTEXITCODE -ne 0) { throw "Diagnostic Guide initialization failed." }

$runnerArgs = @()
switch ($Mode) {
    "Fast" {
        $env:DAP_E2E_MODE = "fast"
        $runnerArgs = @("--guided", "--published-dap", $productionRoot)
    }
    "Visual" {
        $env:DAP_E2E_MODE = "visual"
        $runnerArgs = @("--guided", "--published-dap", $productionRoot)
    }
    "Manual" {
        Remove-Item Env:DAP_E2E_MODE -ErrorAction SilentlyContinue
        $runnerArgs = @("--manual", "--published-dap", $productionRoot)
    }
    "Unguided" {
        Remove-Item Env:DAP_E2E_MODE -ErrorAction SilentlyContinue
        $runnerArgs = @("--unguided")
    }
    "ManualFromStep" {
        if ($Step -lt 1) { throw "Step must be positive." }
        Remove-Item Env:DAP_E2E_MODE -ErrorAction SilentlyContinue
        $runnerArgs = @("--manual-from-step", "$Step", "--published-dap", $productionRoot)
    }
    "VisualFromStep" {
        if ($Step -lt 1) { throw "Step must be positive." }
        Remove-Item Env:DAP_E2E_MODE -ErrorAction SilentlyContinue
        $runnerArgs = @("--visual-from-step", "$Step", "--published-dap", $productionRoot)
    }
}

& $runner @runnerArgs
exit $LASTEXITCODE
