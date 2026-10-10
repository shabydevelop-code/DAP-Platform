param(
    [string]$Output = "C:\GuideMe"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

# Reject an active package before modifying any published files.
# Do not terminate user processes or silently swallow a failed directory removal.
$outputRoot = [System.IO.Path]::GetFullPath($Output).TrimEnd([char[]]@('\', '/'))
$runningFromPackage = @(Get-CimInstance Win32_Process | Where-Object {
    $command = [string]$_.CommandLine
    $executable = [string]$_.ExecutablePath
    $prefix = $outputRoot + '\'
    $executable.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    $command.IndexOf($prefix, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
    $command.IndexOf('"' + $outputRoot + '"', [StringComparison]::OrdinalIgnoreCase) -ge 0
})
if ($runningFromPackage.Count -gt 0) {
    $details = ($runningFromPackage | ForEach-Object {
        "PID $($_.ProcessId): $($_.Name) - $($_.CommandLine)"
    }) -join [Environment]::NewLine
    throw "Cannot publish while processes from $outputRoot are running. Stop these processes and retry:$([Environment]::NewLine)$details"
}
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force -ErrorAction Stop }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

function Publish-Project([string]$Project, [string]$Destination) {
    dotnet publish (Join-Path $repo $Project) -c Release -r win-x64 --self-contained false -o $Destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Project" }
}

Publish-Project "src\DAP.App\DAP.App.csproj" $Output
$nativeHostDir = Join-Path $Output "NativeHost"
New-Item -ItemType Directory -Force -Path $nativeHostDir | Out-Null
Publish-Project "src\DAP.Runtime.Web.NativeHost\DAP.Runtime.Web.NativeHost.csproj" $nativeHostDir

$nativeExe = Join-Path $nativeHostDir "DAP.Runtime.Web.NativeHost.exe"
$nativeConfig = Join-Path $nativeHostDir "DAP.Runtime.Web.NativeHost.runtimeconfig.json"
if (-not (Test-Path $nativeExe) -or -not (Test-Path $nativeConfig)) {
    throw "Native Messaging host publish is incomplete."
}

# Include the unpacked Chrome extension and customer-side registration script.
$extensionSource = Join-Path $repo "src\DAP.Runtime.Web.Extension"
$extensionDestination = Join-Path $Output "ChromeExtension"
$extensionFiles = @(
    "manifest.json",
    "service-worker.js",
    "service-worker-entry.js",
    "content-runtime.js",
    "icon16.png",
    "icon48.png",
    "icon128.png"
)
New-Item -ItemType Directory -Force -Path $extensionDestination | Out-Null
foreach ($file in $extensionFiles) {
    $source = Join-Path $extensionSource $file
    if (-not (Test-Path $source -PathType Leaf)) { throw "Missing Chrome extension file: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $extensionDestination $file) -Force
}
$manifest = Get-Content (Join-Path $extensionDestination "manifest.json") -Raw | ConvertFrom-Json
if ($manifest.background.service_worker -ne "service-worker-entry.js") {
    throw "Unexpected Chrome extension service worker entry."
}
$registerSource = Join-Path $repo "scripts\Register-WebNativeHost.ps1"
Copy-Item -LiteralPath $registerSource -Destination (Join-Path $Output "Register-WebNativeHost.ps1") -Force

Write-Host "Native Host: $nativeExe"
Write-Host "To register the Chrome Native Messaging host, run:"
Write-Host "  powershell -ExecutionPolicy Bypass -File \"$Output\Register-WebNativeHost.ps1\" -PackagePath \"$Output\" -ExtensionId <CHROME_EXTENSION_ID>"

Write-Host ""
Write-Host "Chrome extension: $extensionDestination"
Write-Host "Load unpacked from Chrome extensions, copy its ID, and register with -ExtensionId."
Write-Host "Customer production package created at $Output"
Write-Host "DAP product: $Output\DAP.exe"
