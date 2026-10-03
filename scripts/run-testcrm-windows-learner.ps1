param(
    [string]$GuideId = "testcrm-windows-canonical-workflow"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$backendProject = Join-Path $repoRoot "test-apps\DAP.TestCRM\Server\DAP.TestCRM.Server.csproj"
$windowsProject = Join-Path $repoRoot "test-apps\DAP.TestCRM\Windows\DAP.TestCRM.Windows.csproj"
$dapProject = Join-Path $repoRoot "src\DAP.App\DAP.App.csproj"
$backendUrl = "http://localhost:5201"
$mainWindowAutomationId = "TestCrmMainWindow"

function Stop-OwnedProcessTree($process) {
    if (-not $process) { return }
    try {
        if (-not $process.HasExited) {
            & taskkill.exe /PID $process.Id /T /F 2>$null | Out-Null
        }
    } catch {
        # Cleanup is best-effort. The process may have exited between checks.
    }
}

function Wait-Http([string]$url, [string]$name, $process, [int]$timeoutSeconds = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        if ($process.HasExited) {
            throw "$name exited before becoming ready."
        }

        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        } catch {
            Start-Sleep -Milliseconds 250
        }
    } until ((Get-Date) -ge $deadline)

    throw "$name did not become ready at $url."
}

function Wait-MainWindow($process, [int]$timeoutSeconds = 20) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        if ($process.HasExited) {
            throw "TestCRM Windows exited before its main window became ready."
        }

        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            return
        }

        Start-Sleep -Milliseconds 200
    } until ((Get-Date) -ge $deadline)

    throw "TestCRM Windows main window did not become ready."
}

foreach ($path in @($backendProject, $windowsProject, $dapProject)) {
    if (-not (Test-Path $path)) { throw "Required project not found: $path" }
}

$backend = $null
$windowsApp = $null
$dap = $null
$dapExitCode = $null

try {
    Write-Host "Starting TestCRM backend..."
    $previousUrls = $env:ASPNETCORE_URLS
    $env:ASPNETCORE_URLS = $backendUrl
    try {
        $backend = Start-Process dotnet -ArgumentList @(
            "run", "--project", $backendProject, "--no-launch-profile"
        ) -WorkingDirectory $repoRoot -PassThru -NoNewWindow
    }
    finally {
        $env:ASPNETCORE_URLS = $previousUrls
    }

    Wait-Http "$backendUrl/api/customers" "TestCRM backend" $backend

    Write-Host "Starting TestCRM Windows..."
    $windowsApp = Start-Process dotnet -ArgumentList @(
        "run", "--project", $windowsProject, "--no-launch-profile"
    ) -WorkingDirectory $repoRoot -PassThru

    Wait-MainWindow $windowsApp

    Write-Host ""
    Write-Host "Manual Windows learner run started."
    Write-Host "Guide: $GuideId"
    Write-Host "Perform every learner action yourself in TestCRM."
    Write-Host "The completion UI is owned by DAP."
    Write-Host "Press Ctrl+C here to stop early."
    Write-Host ""

    $dap = Start-Process dotnet -ArgumentList @(
        "run", "--project", $dapProject, "--no-launch-profile", "--",
        "--learner-windows", $GuideId,
        "--window-automation-id", $mainWindowAutomationId,
        "--show-completion"
    ) -WorkingDirectory $repoRoot -PassThru -NoNewWindow

    while (-not $dap.HasExited) {
        Start-Sleep -Milliseconds 200
    }

    $dapExitCode = $dap.ExitCode
}
finally {
    Write-Host ""
    Write-Host "Stopping manual Windows learner run..."
    Stop-OwnedProcessTree $dap
    Stop-OwnedProcessTree $windowsApp
    Stop-OwnedProcessTree $backend
}

if ($null -ne $dapExitCode) {
    exit $dapExitCode
}
