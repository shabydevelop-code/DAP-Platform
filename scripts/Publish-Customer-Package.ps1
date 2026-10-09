param(
    [string]$Output = "C:\DAP-Production"
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

Write-Host ""
Write-Host "Customer production package created at $Output"
Write-Host "DAP product: $Output\DAP.exe"
