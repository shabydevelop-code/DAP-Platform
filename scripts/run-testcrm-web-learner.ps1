param(
    [ValidateSet("chrome", "edge")]
    [string]$Browser = "chrome",
    [string]$GuideId = "testcrm-web-canonical-workflow",
    [ValidateRange(1, 2147483647)]
    [int]$StartStep = 1
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$backendProject = Join-Path $repoRoot "test-apps\DAP.TestCRM\Server\DAP.TestCRM.Server.csproj"
$webProject = Join-Path $repoRoot "test-apps\DAP.TestCRM\Web\DAP.TestCRM.Web.csproj"
$dapProject = Join-Path $repoRoot "src\DAP.App\DAP.App.csproj"
$backendProjectDir = Split-Path -Parent $backendProject
$webProjectDir = Split-Path -Parent $webProject
$dapProjectDir = Split-Path -Parent $dapProject
$backendUrl = "http://localhost:5201"
$testCrmUrl = "http://localhost:5200"
$runRoot = Join-Path $env:TEMP ("DAP\ManualLearner\Web\" + [Guid]::NewGuid())
$backendOutput = Join-Path $runRoot "Server"
$webOutput = Join-Path $runRoot "Web"
$dapOutput = Join-Path $runRoot "DAP"
$profileDir = Join-Path $runRoot "BrowserProfile"

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

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

function Build-Isolated([string]$project, [string]$output, [string]$name) {
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    Write-Host "Building $name into isolated manual-run output..."
    & dotnet build $project --nologo --verbosity minimal --output $output
    if ($LASTEXITCODE -ne 0) {
        throw "$name build failed."
    }
}

foreach ($path in @($backendProject, $webProject, $dapProject)) {
    if (-not (Test-Path $path)) { throw "Required project not found: $path" }
}

Assert-PortFree 5201 "TestCRM backend"
Assert-PortFree 5200 "TestCRM Web"

$browserPath = Resolve-BrowserPath $Browser
$cdpPort = Get-FreeTcpPort
New-Item -ItemType Directory -Force -Path $profileDir | Out-Null

$backend = $null
$web = $null
$browserProcess = $null
$dap = $null
$dapExitCode = $null

try {
    Build-Isolated $backendProject $backendOutput "TestCRM Server"
    Build-Isolated $webProject $webOutput "TestCRM Web"
    Build-Isolated $dapProject $dapOutput "DAP"

    $backendDll = Join-Path $backendOutput "DAP.TestCRM.Server.dll"
    $webDll = Join-Path $webOutput "DAP.TestCRM.Web.dll"
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

    Write-Host "Starting TestCRM Web..."
    $previousUrls = $env:ASPNETCORE_URLS
    $previousBackendUrl = $env:TestCrmBackendUrl
    $env:ASPNETCORE_URLS = $testCrmUrl
    $env:TestCrmBackendUrl = $backendUrl
    try {
        $web = Start-Process dotnet -ArgumentList @($webDll) -WorkingDirectory $webProjectDir -PassThru -NoNewWindow
    }
    finally {
        $env:ASPNETCORE_URLS = $previousUrls
        $env:TestCrmBackendUrl = $previousBackendUrl
    }

    Wait-Http $testCrmUrl "TestCRM Web" $web

    $startUrl = $testCrmUrl
    if ($GuideId -eq "testcrm-web-canonical-workflow" -and $StartStep -eq 53) {
        $startUrl = "$testCrmUrl/#/site/1/cases"
        Write-Host "Preparing TestCRM state for Step 53: site Cases screen."
    } elseif ($StartStep -gt 1) {
        throw "Focused TestCRM setup is not defined for Guide '$GuideId' Step $StartStep. Refusing to start from an unrelated application state."
    }

    Write-Host "Opening $Browser for manual learner run..."
    $browserProcess = Start-Process $browserPath -ArgumentList @(
        "--remote-debugging-port=$cdpPort",
        "--user-data-dir=$profileDir",
        "--start-maximized",
        $startUrl
    ) -PassThru

    $cdpEndpoint = "http://127.0.0.1:$cdpPort"
    Wait-Http "$cdpEndpoint/json/version" "$Browser CDP" $browserProcess 15

    Write-Host ""
    Write-Host "Manual Web learner run started."
    Write-Host "Guide: $GuideId"
    Write-Host "Browser: $Browser"
    Write-Host "Start step: $StartStep"
    Write-Host "Perform every learner action yourself in the browser."
    Write-Host "The completion UI is owned by DAP."
    Write-Host "Press Ctrl+C here to stop early."
    Write-Host ""

    $dapArgs = @(
        $dapDll,
        "--learner-web", $GuideId,
        "--cdp", $cdpEndpoint,
        "--page-url-contains", "localhost:5200"
    )
    if ($StartStep -gt 1) {
        $dapArgs += @("--start-step", [string]$StartStep)
    }

    $dap = Start-Process dotnet -ArgumentList $dapArgs -WorkingDirectory $dapProjectDir -PassThru -NoNewWindow

    while (-not $dap.HasExited) {
        Start-Sleep -Milliseconds 200
    }

    $dapExitCode = $dap.ExitCode
}
finally {
    Write-Host ""
    Write-Host "Stopping manual Web learner run..."
    Stop-OwnedProcessTree $dap
    Stop-OwnedProcessTree $web
    Stop-OwnedProcessTree $backend
    Stop-OwnedProcessTree $browserProcess
    if (Test-Path $runRoot) {
        Remove-Item -Recurse -Force $runRoot -ErrorAction SilentlyContinue
    }
}

if ($null -ne $dapExitCode) {
    exit $dapExitCode
}
