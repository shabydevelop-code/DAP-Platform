param([string]$Url = 'http://127.0.0.1:53922')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'tests\Instructor.Accessibility'
try {
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
    if ($response.StatusCode -ne 200) { throw "HTTP $($response.StatusCode)" }
} catch {
    throw "Start the installed Instructor before running the accessibility checks: $Url. $($_.Exception.Message)"
}
Push-Location $dir
try {
    if (-not (Test-Path 'node_modules')) {
        npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
    }
    npx playwright install chromium
    if ($LASTEXITCODE -ne 0) { throw 'Playwright Chromium installation failed' }
    npx playwright test
    if ($LASTEXITCODE -ne 0) { throw 'Instructor accessibility regression failed. See tests\Instructor.Accessibility\playwright-report.' }
} finally { Pop-Location }
