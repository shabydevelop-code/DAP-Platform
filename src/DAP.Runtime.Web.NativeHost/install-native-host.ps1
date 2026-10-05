param(
  [Parameter(Mandatory=$true)][string]$ExtensionId,
  [string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "DAP.Runtime.Web.NativeHost.csproj"
dotnet build $project -c $Configuration
$exe = Join-Path $PSScriptRoot "bin\$Configuration\net8.0\DAP.Runtime.Web.NativeHost.exe"
if (!(Test-Path $exe)) { throw "Native host executable was not produced: $exe" }
$manifestDir = Join-Path $env:LOCALAPPDATA "DAP\NativeMessaging"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
$manifestPath = Join-Path $manifestDir "com.dap.web_runtime.json"
$manifest = @{
  name="com.dap.web_runtime"
  description="DAP Web Runtime Native Messaging Host"
  path=(Resolve-Path $exe).Path
  type="stdio"
  allowed_origins=@("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8
$keys=@(
 "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.dap.web_runtime",
 "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.dap.web_runtime"
)
foreach($key in $keys){New-Item -Force -Path $key | Out-Null; Set-Item -Path $key -Value $manifestPath}
Write-Host "DAP native host registered for Chrome and Edge."
Write-Host "Manifest: $manifestPath"
Write-Host "Executable: $exe"
