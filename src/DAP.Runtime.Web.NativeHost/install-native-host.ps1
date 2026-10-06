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
foreach($key in $keys){
  New-Item -Force -Path $key | Out-Null
  Set-Item -Path $key -Value $manifestPath
  $actual=(Get-Item $key).GetValue("")
  if($actual -ne $manifestPath){throw "Native Messaging registry verification failed for $key"}
}
$written=Get-Content $manifestPath -Raw | ConvertFrom-Json
if([IO.Path]::GetFullPath([string]$written.path) -ne [IO.Path]::GetFullPath($exe)){
  throw "Native Messaging manifest path verification failed."
}
if(-not (@($written.allowed_origins) -contains "chrome-extension://$ExtensionId/")){
  throw "Native Messaging allowed_origins verification failed."
}
Write-Host "DAP native host registered for Chrome and Edge."
Write-Host "Manifest: $manifestPath"
Write-Host "Executable: $exe"
