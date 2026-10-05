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

# The Native Host executable registered with Chrome/Edge lives in the project's
# normal bin output. Stop only that host process before rebuilding it.
Get-Process "DAP.Runtime.Web.NativeHost" -ErrorAction SilentlyContinue |
    ForEach-Object {
        Stop-Process -Id $_.Id -Force
        $_.WaitForExit(5000)
    }

Write-Host "Building production Native Host..."
dotnet build ".\src\DAP.Runtime.Web.NativeHost\DAP.Runtime.Web.NativeHost.csproj" --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Native Host build failed." }

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

Write-Host "Opening $Browser profile '$($browserInfo.Profile)' with the production DAP extension..."
Start-Process $browserInfo.Exe -ArgumentList @(
    "--profile-directory=$($browserInfo.Profile)",
    "--new-window",
    "http://localhost:5200/"
)

# No E2E session id is set. The production learner therefore uses its normal
# extension adapter path and receives no information from an E2E/test driver.
Remove-Item Env:DAP_WEB_SESSION_ID -ErrorAction SilentlyContinue
Remove-Item Env:DAP_E2E_MODE -ErrorAction SilentlyContinue

Write-Host "Starting autonomous DAP Web Learner for Guide '$GuideKey'..."
$dap = Start-Process dotnet -ArgumentList @(
    "run","--no-build",
    "--project",".\src\DAP.App\DAP.App.csproj",
    "--","--learner-web",$GuideKey
) -WorkingDirectory $repoRoot -PassThru

Write-Host ""
Write-Host "Autonomous learner session started."
Write-Host "TestCRM Web PID: $($web.Id)"
Write-Host "TestCRM Backend PID: $($backend.Id)"
Write-Host "DAP Learner PID: $($dap.Id)"
Write-Host "This launcher now exits. No E2E Runner remains active."
