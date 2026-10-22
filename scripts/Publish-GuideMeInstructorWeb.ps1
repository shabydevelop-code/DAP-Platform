param(
    [string]$Destination = 'C:\GuideMe\InstructorWeb',
    [string]$ExtensionDestination = 'C:\GuideMe\InstructorChromeExtension'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\GuideMe.Instructor.Web\GuideMe.Instructor.Web.csproj'
$source = Join-Path $root 'src\GuideMe.Instructor.ChromeExtension'

if (-not (Test-Path $project) -or -not (Test-Path $source)) {
    throw 'GuideMe Instructor Web sources are missing.'
}

dotnet publish $project -c Release -o $Destination
if ($LASTEXITCODE -ne 0) {
    throw "Instructor Web publish failed (exit $LASTEXITCODE). Extension was not modified."
}

New-Item -ItemType Directory -Path $ExtensionDestination -Force | Out-Null
$files = Get-ChildItem $source -File | Where-Object {
    $_.Extension -in @('.js', '.json', '.html', '.css', '.png', '.md')
}
foreach ($file in $files) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ExtensionDestination $file.Name) -Force
}
foreach ($file in $files) {
    $target = Join-Path $ExtensionDestination $file.Name
    $a = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $b = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($a -ne $b) { throw "Instructor extension sync verification failed: $($file.Name)" }
}
Write-Host "PASS: Instructor Web published to $Destination"
Write-Host "PASS: $($files.Count) Instructor extension files synchronized to $ExtensionDestination"
Write-Host 'Reload the unpacked GuideMe Instructor extension at chrome://extensions.'
Write-Host "Start Web Instructor: & `"$Destination\GuideMe.Instructor.Web.exe`""
