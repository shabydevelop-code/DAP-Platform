param(
    [ValidateSet("chrome", "edge")]
    [string]$Browser = "chrome",
    [string]$GuideId = "testcrm-create-case"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testCrmProject = Join-Path $repoRoot "test-apps\DAP.TestCRM\DAP.TestCRM.csproj"
$dapProject = Join-Path $repoRoot "src\DAP.App\DAP.App.csproj"
$testCrmUrl = "http://localhost:5200"
$manualOutputRoot = Join-Path $env:TEMP "DAP\ManualLearner"
$testCrmOutput = Join-Path $manualOutputRoot "TestCRM"
$testCrmExe = Join-Path $testCrmOutput "DAP.TestCRM.exe"

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Stop-OwnedProcessTree($process) {
    if (-not $process) { return }
    try {
        if (-not $process.HasExited) {
            # Start-Process may own a dotnet/browser process that has children.
            # Kill only this launcher's process tree; never touch unrelated DAP,
            # dotnet, Chrome, or Edge processes.
            & taskkill.exe /PID $process.Id /T /F 2>$null | Out-Null
        }
    } catch {
        # Cleanup is best-effort. The process may have exited between checks.
    }
}

function Resolve-BrowserPath([string]$name) {
    $programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
    if ($name -eq "chrome") {
        $candidates = @(
            (Join-Path $env:ProgramFiles "Google\Chrome\Application\chrome.exe"),
            (Join-Path $programFilesX86 "Google\Chrome\Application\chrome.exe"),
            (Join-Path $env:LOCALAPPDATA "Google\Chrome\Application\chrome.exe")
        )
    } else {
        $candidates = @(
            (Join-Path $env:ProgramFiles "Microsoft\Edge\Application\msedge.exe"),
            (Join-Path $programFilesX86 "Microsoft\Edge\Application\msedge.exe")
        )
    }
    $path = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $path) { throw "Could not find installed $name." }
    return $path
}

if (-not (Test-Path $testCrmProject)) { throw "TestCRM project not found: $testCrmProject" }
if (-not (Test-Path $dapProject)) { throw "DAP.App project not found: $dapProject" }

$browserPath = Resolve-BrowserPath $Browser
$cdpPort = Get-FreeTcpPort
$profileDir = Join-Path $env:TEMP ("DAP\ManualLearner\" + [Guid]::NewGuid())
New-Item -ItemType Directory -Force -Path $profileDir | Out-Null

$crm = $null
$browserProcess = $null
$dap = $null

try {
    # Build TestCRM into a Manual-Learner-owned output. Never execute it from
    # the project's normal bin\Debug directory: an interrupted prior manual run
    # must not lock normal development builds.
    New-Item -ItemType Directory -Force -Path $testCrmOutput | Out-Null
    Write-Host "Building TestCRM for manual learner run..."
    & dotnet build $testCrmProject --nologo --verbosity quiet --output $testCrmOutput
    if ($LASTEXITCODE -ne 0) { throw "TestCRM build failed." }
    if (-not (Test-Path $testCrmExe)) { throw "TestCRM executable not found: $testCrmExe" }

    Write-Host "Starting TestCRM..."
    $crm = Start-Process $testCrmExe -WorkingDirectory $testCrmOutput -PassThru

    $deadline = (Get-Date).AddSeconds(30)
    $ready = $false
    do {
        try {
            $response = Invoke-WebRequest -Uri $testCrmUrl -UseBasicParsing -TimeoutSec 1
            $ready = $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
        } catch {
            Start-Sleep -Milliseconds 250
        }
    } until ($ready -or (Get-Date) -ge $deadline)
    if (-not $ready) { throw "TestCRM did not become ready at $testCrmUrl." }

    Write-Host "Opening $Browser for manual learner run..."
    $browserProcess = Start-Process $browserPath -ArgumentList @(
        "--remote-debugging-port=$cdpPort",
        "--user-data-dir=$profileDir",
        "--start-maximized",
        $testCrmUrl
    ) -PassThru

    $cdpEndpoint = "http://127.0.0.1:$cdpPort"
    $deadline = (Get-Date).AddSeconds(15)
    $cdpReady = $false
    do {
        try {
            Invoke-WebRequest -Uri "$cdpEndpoint/json/version" -UseBasicParsing -TimeoutSec 1 | Out-Null
            $cdpReady = $true
        } catch {
            Start-Sleep -Milliseconds 200
        }
    } until ($cdpReady -or (Get-Date) -ge $deadline)
    if (-not $cdpReady) { throw "$Browser did not expose CDP at $cdpEndpoint." }

    Write-Host ""
    Write-Host "Manual learner run started."
    Write-Host "Guide: $GuideId"
    Write-Host "Browser: $Browser"
    Write-Host "Perform every learner action yourself in the browser."
    Write-Host "Close DAP or press Ctrl+C here to stop."
    Write-Host ""

    $dap = Start-Process dotnet -ArgumentList @(
        "run", "--project", $dapProject, "--",
        "--learner-web", $GuideId,
        "--cdp", $cdpEndpoint,
        "--page-url-contains", "localhost:5200"
    ) -PassThru -NoNewWindow

    # Do not block in Process.WaitForExit(): a blocking .NET call prevents
    # PowerShell from handling Ctrl+C promptly and therefore delays finally.
    # Poll with an interruptible PowerShell command instead.
    while (-not $dap.HasExited) {
        Start-Sleep -Milliseconds 200
    }

    $dapExitCode = $dap.ExitCode
}
finally {
    Write-Host ""
    Write-Host "Stopping manual learner run..."
    Stop-OwnedProcessTree $dap
    Stop-OwnedProcessTree $crm
    Stop-OwnedProcessTree $browserProcess
    if (Test-Path $profileDir) { Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue }
}

if ($null -ne $dapExitCode) {
    exit $dapExitCode
}
