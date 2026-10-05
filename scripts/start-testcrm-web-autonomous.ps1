param(
    [string]$GuideKey = "testcrm-web-canonical-workflow",
    [ValidateSet("chrome","edge")]
    [string]$Browser = "chrome"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Assert-PortFree([int]$Port) {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try {
        $listener.Start()
    }
    catch {
        throw "Port $Port is already in use. Stop the existing process before starting the autonomous learner session."
    }
    finally {
        try { $listener.Stop() } catch {}
    }
}

function Wait-Port([int]$Port, [int]$TimeoutMs = 5000) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $task = $client.ConnectAsync("127.0.0.1", $Port)
            if ($task.Wait(150) -and $client.Connected) { return }
        }
        catch {}
        finally { $client.Dispose() }
        Start-Sleep -Milliseconds 100
    }
    throw "Port $Port did not become ready within 5 seconds."
}

function Resolve-ExtensionRegistration {
    $manifestPath = Join-Path $env:LOCALAPPDATA "DAP\NativeMessaging\com.dap.web_runtime.json"
    if (-not (Test-Path $manifestPath)) {
        throw "DAP Native Messaging registration was not found. Run install-native-host.ps1 first."
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $origin = @($manifest.allowed_origins) |
        Where-Object { $_ -match '^chrome-extension://([a-z]+)/?$' } |
        Select-Object -First 1

    if (-not $origin) {
        throw "DAP Native Messaging manifest does not contain a valid chrome-extension origin."
    }

    [pscustomobject]@{
        ManifestPath = $manifestPath
        ExtensionId = [regex]::Match($origin, '^chrome-extension://([a-z]+)/?$').Groups[1].Value
    }
}

function Resolve-BrowserInfo([string]$BrowserName, [string]$ExtensionId) {
    if ($BrowserName -eq "edge") {
        $userData = Join-Path $env:LOCALAPPDATA "Microsoft\Edge\User Data"
        $candidates = @(
            "$env:ProgramFiles(x86)\Microsoft\Edge\Application\msedge.exe",
            "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
        )
    }
    else {
        $userData = Join-Path $env:LOCALAPPDATA "Google\Chrome\User Data"
        $candidates = @(
            "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
            "$env:ProgramFiles(x86)\Google\Chrome\Application\chrome.exe",
            "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
        )
    }

    $exe = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $exe) {
        throw "Could not find the selected browser executable."
    }
    if (-not (Test-Path $userData)) {
        throw "Browser user-data directory was not found: $userData"
    }

    $profile = Get-ChildItem $userData -Directory |
        Where-Object { $_.Name -eq "Default" -or $_.Name -like "Profile *" } |
        ForEach-Object {
            $profileDir = $_
            foreach ($fileName in @("Preferences", "Secure Preferences")) {
                $path = Join-Path $profileDir.FullName $fileName
                if (Test-Path $path) {
                    try {
                        if ((Get-Content $path -Raw).IndexOf($ExtensionId, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                            return $profileDir.Name
                        }
                    }
                    catch {}
                }
            }
        } |
        Select-Object -First 1

    if (-not $profile) {
        throw "Could not find a browser profile containing DAP extension '$ExtensionId'."
    }

    [pscustomobject]@{
        Exe = $exe
        Profile = $profile
    }
}

Assert-PortFree 5200
Assert-PortFree 5201

$registration = Resolve-ExtensionRegistration
$browserInfo = Resolve-BrowserInfo $Browser $registration.ExtensionId

# Autonomous product verification must not tear down the production browser
# transport as a side effect of test/development startup. If a Native Host is
# already running, preserve it. Reinstall only when registration/executable is
# missing; code updates to the Native Host are an explicit install step.
$nativeHostProcesses = @(Get-Process "DAP.Runtime.Web.NativeHost" -ErrorAction SilentlyContinue)

$registeredManifest = Get-Content $registration.ManifestPath -Raw | ConvertFrom-Json
$registeredExe = [IO.Path]::GetFullPath([string]$registeredManifest.path)

if (-not (Test-Path $registeredExe)) {
    Write-Host "Native Host registration is missing or stale; installing..."
    & ".\src\DAP.Runtime.Web.NativeHost\install-native-host.ps1" -ExtensionId $registration.ExtensionId -Configuration "Debug"
    if ($LASTEXITCODE -ne 0) { throw "Native Host registration failed." }
    $registeredManifest = Get-Content $registration.ManifestPath -Raw | ConvertFrom-Json
    $registeredExe = [IO.Path]::GetFullPath([string]$registeredManifest.path)
}

if (-not (Test-Path $registeredExe)) {
    throw "Registered Native Host executable does not exist: $registeredExe"
}

if ($nativeHostProcesses.Count -gt 0) {
    Write-Host "Preserving running production Native Host PID(s): $($nativeHostProcesses.Id -join ', ')"
} else {
    Write-Host "No Native Host process is currently running; the browser extension will start it through Native Messaging."
}

Write-Host "Native Host registered executable: $registeredExe"

Write-Host "Building DAP Learner..."
dotnet build ".\src\DAP.App\DAP.App.csproj" --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "DAP build failed." }

Write-Host "Starting TestCRM backend..."
$backend = Start-Process dotnet -ArgumentList @(
    "run","--no-build",
    "--project",".\test-apps\DAP.TestCRM\Server\DAP.TestCRM.Server.csproj",
    "--","--urls","http://localhost:5201"
) -WorkingDirectory $repoRoot -PassThru

Write-Host "Starting TestCRM Web..."
$web = Start-Process dotnet -ArgumentList @(
    "run","--no-build",
    "--project",".\test-apps\DAP.TestCRM\Web\DAP.TestCRM.Web.csproj",
    "--","--urls","http://localhost:5200"
) -WorkingDirectory $repoRoot -PassThru

try {
    Wait-Port 5201
    Wait-Port 5200
}
catch {
    try { Stop-Process -Id $web.Id -Force -ErrorAction SilentlyContinue } catch {}
    try { Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue } catch {}
    throw
}

# No E2E session id is set. The production learner therefore uses its normal
# extension adapter path and receives no information from an E2E/test driver.
Remove-Item Env:DAP_WEB_SESSION_ID -ErrorAction SilentlyContinue
Remove-Item Env:DAP_E2E_MODE -ErrorAction SilentlyContinue

$diagnosticRoot = Join-Path $env:TEMP ("DAP\AutonomousWeb\" + [DateTime]::Now.ToString("yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $diagnosticRoot -Force | Out-Null
$dapStdout = Join-Path $diagnosticRoot "dap.stdout.log"
$dapStderr = Join-Path $diagnosticRoot "dap.stderr.log"

# Start the production learner before opening the target page. This guarantees
# that the runtime named-pipe server already exists when the browser extension
# starts/reconnects its Native Host.
Write-Host "Starting autonomous DAP Web Learner for Guide '$GuideKey'..."
$dap = Start-Process dotnet -ArgumentList @(
    "run","--no-build",
    "--project",".\src\DAP.App\DAP.App.csproj",
    "--","--learner-web",$GuideKey
) -WorkingDirectory $repoRoot -RedirectStandardOutput $dapStdout -RedirectStandardError $dapStderr -PassThru

$pipeDeadline = [DateTime]::UtcNow.AddSeconds(5)
$pipeReady = $false
while ([DateTime]::UtcNow -lt $pipeDeadline) {
    if ($dap.HasExited) {
        $stderr = if (Test-Path $dapStderr) { Get-Content $dapStderr -Raw } else { "" }
        $stdout = if (Test-Path $dapStdout) { Get-Content $dapStdout -Raw } else { "" }
        throw "DAP Learner exited during autonomous startup. STDERR:$([Environment]::NewLine)$stderr$([Environment]::NewLine)STDOUT:$([Environment]::NewLine)$stdout"
    }
    if (Test-Path $dapStderr) {
        $stderrNow = Get-Content $dapStderr -Raw
        if ($stderrNow -match '\[DAP runtime\] Web pipe server waiting: dap-web-runtime-v1') {
            $pipeReady = $true
            break
        }
        if ($stderrNow -match '\[DAP runtime\] Web pipe accept (?:loop faulted|error):') {
            throw "DAP Web runtime pipe server failed to start. STDERR:$([Environment]::NewLine)$stderrNow"
        }
    }
    Start-Sleep -Milliseconds 50
}
if (-not $pipeReady) {
    $stderr = if (Test-Path $dapStderr) { Get-Content $dapStderr -Raw } else { "" }
    throw "DAP Web runtime pipe server did not become ready within 5 seconds. STDERR:$([Environment]::NewLine)$stderr"
}

Write-Host "Production Runtime pipe server ready."
Write-Host "Opening TestCRM in $Browser profile '$($browserInfo.Profile)'..."
# Do not force --new-window. Reuse the selected profile's existing browser
# window when one exists; otherwise Chrome/Edge creates the first window.
$profileArgument = '--profile-directory="' + $browserInfo.Profile + '"'
Start-Process -FilePath $browserInfo.Exe -ArgumentList @(
    $profileArgument,
    "http://localhost:5200/"
)

$transportDeadline = [DateTime]::UtcNow.AddSeconds(5)
$transportReady = $false
while ([DateTime]::UtcNow -lt $transportDeadline) {
    if ($dap.HasExited) {
        $stderr = if (Test-Path $dapStderr) { Get-Content $dapStderr -Raw } else { "" }
        $stdout = if (Test-Path $dapStdout) { Get-Content $dapStdout -Raw } else { "" }
        throw "DAP Learner exited after browser startup. STDERR:$([Environment]::NewLine)$stderr$([Environment]::NewLine)STDOUT:$([Environment]::NewLine)$stdout"
    }

    if (Test-Path $dapStderr) {
        $stderrNow = Get-Content $dapStderr -Raw
        if ($stderrNow -match '\[DAP runtime\] selected browser extension host\.') {
            $transportReady = $true
            break
        }
    }

    Start-Sleep -Milliseconds 100
}

if (-not $transportReady) {
    $stderr = if (Test-Path $dapStderr) { Get-Content $dapStderr -Raw } else { "" }
    $nativeHostLog = Join-Path $env:LOCALAPPDATA "DAP\Logs\native-host.log"
    $nativeTail = if (Test-Path $nativeHostLog) {
        (Get-Content $nativeHostLog -Tail 80) -join [Environment]::NewLine
    } else {
        "<native-host.log not created>"
    }
    $nativePids = @(Get-Process "DAP.Runtime.Web.NativeHost" -ErrorAction SilentlyContinue).Id
    $nativeState = if ($nativePids.Count -gt 0) {
        "running PID(s): " + ($nativePids -join ", ")
    } else {
        "no Native Host process is running"
    }
    throw "DAP Learner did not establish the production browser-extension transport within 5 seconds. Native Host state: $nativeState. DAP STDERR:$([Environment]::NewLine)$stderr$([Environment]::NewLine)NATIVE HOST LOG:$([Environment]::NewLine)$nativeTail"
}

Write-Host ""
Write-Host "Production extension transport connected."
Write-Host "Autonomous learner session started."
Write-Host "TestCRM Web PID: $($web.Id)"
Write-Host "TestCRM Backend PID: $($backend.Id)"
Write-Host "DAP Learner PID: $($dap.Id)"
Write-Host "DAP diagnostics: $diagnosticRoot"
Write-Host "This launcher now exits. No E2E Runner remains active."
