param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$expected = [System.IO.Path]::GetFullPath($ExecutablePath)
$active = @(Get-Process -Name 'GuideMe' -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -and [string]::Equals(
            [System.IO.Path]::GetFullPath($_.Path),
            $expected,
            [System.StringComparison]::OrdinalIgnoreCase)
    } catch {
        $false
    }
})

if ($active.Count -gt 0) {
    $ids = ($active | ForEach-Object { $_.Id }) -join ', '
    [Console]::Error.WriteLine("GuideMe BUILD BLOCKED: This build's GuideMe.exe is still running (PID: $ids).")
    [Console]::Error.WriteLine("Close the current learner session and build again. The running executable is: $expected")
    exit 1
}
exit 0
