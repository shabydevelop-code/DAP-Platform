param(
    [string]$Output = "C:\DAP-Production"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

Get-Process DAP -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $Output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Output | Out-Null

function Publish-Project([string]$Project, [string]$Destination) {
    dotnet publish (Join-Path $repo $Project) -c Release -r win-x64 --self-contained false -o $Destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Project" }
}

Publish-Project "src\DAP.App\DAP.App.csproj" $Output

$diag = Join-Path $Output "Diagnostics"
Publish-Project "test-apps\DAP.TestCRM\Server\DAP.TestCRM.Server.csproj" (Join-Path $diag "TestCRM\Server")
Publish-Project "test-apps\DAP.TestCRM\Web\DAP.TestCRM.Web.csproj" (Join-Path $diag "TestCRM\Web")
Publish-Project "test-apps\DAP.TestCRM\Windows\DAP.TestCRM.Windows.csproj" (Join-Path $diag "TestCRM\Windows")
Publish-Project "tests\DAP.E2E\DAP.E2E.csproj" (Join-Path $diag "Runners\Unified")

Copy-Item (Join-Path $PSScriptRoot "Run-Diagnostics.ps1") $diag
Copy-Item (Join-Path $PSScriptRoot "Initialize-Diagnostics.ps1") $diag

Write-Host ""
Write-Host "Customer production package created at $Output"
Write-Host "DAP product: $Output\DAP.exe"
Write-Host "Diagnostics: $diag"
