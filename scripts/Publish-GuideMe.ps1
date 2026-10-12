param(
    [string]$Destination = 'C:\GuideMe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\DAP.App\DAP.App.csproj'
$extensionSource = Join-Path $root 'src\DAP.Runtime.Web.Extension'
$extensionDestination = Join-Path $Destination 'ChromeExtension'

if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
    throw "App project not found: $appProject"
}

& dotnet publish $appProject -c Release -r win-x64 --self-contained false -o $Destination
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    throw "GuideMe publish failed with exit code $publishExitCode."
}

if (-not (Test-Path -LiteralPath $extensionDestination -PathType Container)) {
    throw "Chrome extension deployment directory not found: $extensionDestination. Existing extension files were not modified."
}

foreach ($name in @('icon16.png', 'icon48.png', 'icon128.png')) {
    $source = Join-Path $extensionSource $name
    $target = Join-Path $extensionDestination $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Source icon not found: $source"
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw "Icon verification failed: $name"
    }
}

Write-Host 'PASS: GuideMe published and Chrome extension icons synchronized.'
