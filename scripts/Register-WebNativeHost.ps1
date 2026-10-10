param(
    [string]$PackagePath = "C:\GuideMe",
    [Parameter(Mandatory=$true)][string]$ExtensionId
)

$ErrorActionPreference = "Stop"
$package = [System.IO.Path]::GetFullPath($PackagePath)
$exe = Join-Path $package "NativeHost\DAP.Runtime.Web.NativeHost.exe"
$config = Join-Path $package "NativeHost\DAP.Runtime.Web.NativeHost.runtimeconfig.json"
if (-not (Test-Path $exe) -or -not (Test-Path $config)) {
    throw "Native Host executable or runtime configuration is missing from $package"
}
if ($ExtensionId -notmatch '^[a-p]{32}$') {
    throw "Invalid Chrome extension ID."
}

$manifestDir = Join-Path $env:LOCALAPPDATA "DAP\NativeMessaging"
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
$manifestPath = Join-Path $manifestDir "com.dap.web_runtime.json"
$manifest = [ordered]@{
    name = "com.dap.web_runtime"
    description = "GuideMe browser Native Messaging Host"
    path = $exe
    type = "stdio"
    allowed_origins = @("chrome-extension://$ExtensionId/")
}
# Chrome Native Messaging manifests must be UTF-8 without a BOM.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $utf8NoBom)

$registryPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.dap.web_runtime"
New-Item -Path $registryPath -Force | Out-Null
Set-Item -Path $registryPath -Value $manifestPath

Write-Host "Registered GuideMe Native Messaging host:"
Write-Host "  Executable: $exe"
Write-Host "  Manifest:   $manifestPath"
Write-Host "Restart Chrome to use the new host."
