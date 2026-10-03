param(
    [string]$GuideId = "testcrm-windows-canonical-workflow"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$backendProject = Join-Path $repoRoot "test-apps\DAP.TestCRM\Server\DAP.TestCRM.Server.csproj"
$windowsProject = Join-Path $repoRoot "test-apps\DAP.TestCRM\Windows\DAP.TestCRM.Windows.csproj"
$dapProject = Join-Path $repoRoot "src\DAP.App\DAP.App.csproj"
$backendProjectDir = Split-Path -Parent $backendProject
$windowsProjectDir = Split-Path -Parent $windowsProject
$dapProjectDir = Split-Path -Parent $dapProject
$backendUrl = "http://localhost:5201"
$mainWindowAutomationId = "TestCrmMainWindow"
$runRoot = Join-Path $env:TEMP ("DAP\ManualLearner\Windows\" + [Guid]::NewGuid())
$backendOutput = Join-Path $runRoot "Server"
$windowsOutput = Join-Path $runRoot "Windows"
$dapOutput = Join-Path $runRoot "DAP"

function Assert-PortFree([int]$port, [string]$name) {
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
        $listener.Start()
    }
    catch {
        throw "$name cannot start because port $port is already in use. Close the previous TestCRM/manual/E2E run and try again."
    }
    finally {
        if ($listener) { $listener.Stop() }
    }
}

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

function Build-Isolated([string]$project, [string]$output, [string]$name) {
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    Write-Host "Building $name into isolated manual-run output..."
    & dotnet build $project --nologo --verbosity minimal --output $output
    if ($LASTEXITCODE -ne 0) {
        throw "$name build failed."
    }
}

foreach ($path in @($backendProject, $windowsProject, $dapProject)) {
    if (-not (Test-Path $path)) { throw "Required project not found: $path" }
}

Assert-PortFree 5201 "TestCRM backend"

$backend = $null
$windowsApp = $null
$dap = $null
$dapExitCode = $null

try {
    Build-Isolated $backendProject $backendOutput "TestCRM Server"
    Build-Isolated $windowsProject $windowsOutput "TestCRM Windows"
    Build-Isolated $dapProject $dapOutput "DAP"

    $backendDll = Join-Path $backendOutput "DAP.TestCRM.Server.dll"
    $windowsExe = Join-Path $windowsOutput "DAP.TestCRM.Windows.exe"
    $dapDll = Join-Path $dapOutput "DAP.dll"

    Write-Host "Starting TestCRM backend..."
    $previousUrls = $env:ASPNETCORE_URLS
    $env:ASPNETCORE_URLS = $backendUrl
    try {
        $backend = Start-Process dotnet -ArgumentList @($backendDll) -WorkingDirectory $backendProjectDir -PassThru -NoNewWindow
    }
    finally {
        $env:ASPNETCORE_URLS = $previousUrls
    }

    Wait-Http "$backendUrl/api/customers" "TestCRM backend" $backend

    Write-Host "Starting TestCRM Windows..."
    $windowsApp = Start-Process $windowsExe -WorkingDirectory $windowsProjectDir -PassThru
    Wait-MainWindow $windowsApp

    Write-Host ""
    Write-Host "Manual Windows learner run started."
    Write-Host "Guide: $GuideId"
    Write-Host "Perform every learner action yourself in TestCRM."
    Write-Host "The completion UI is owned by DAP."
    Write-Host "Press Ctrl+C here to stop early."
    Write-Host ""

    $dap = Start-Process dotnet -ArgumentList @(
        $dapDll,
        "--learner-windows", $GuideId,
        "--window-automation-id", $mainWindowAutomationId,
        "--show-completion"
    ) -WorkingDirectory $dapProjectDir -PassThru -NoNewWindow

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
    if (Test-Path $runRoot) {
        Remove-Item -Recurse -Force $runRoot -ErrorAction SilentlyContinue
    }
}

if ($null -ne $dapExitCode) {
    exit $dapExitCode
}
