param(
    [string]$Destination = 'C:\GuideMe',
    [switch]$SkipExtensionSync
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\DAP.App\DAP.App.csproj'
$nativeHostProject = Join-Path $root 'src\DAP.Runtime.Web.NativeHost\DAP.Runtime.Web.NativeHost.csproj'
$extensionSource = Join-Path $root 'src\DAP.Runtime.Web.Extension'
$extensionDestination = Join-Path $Destination 'ChromeExtension'

if ($SkipExtensionSync -and [string]::Equals(
    [System.IO.Path]::GetFullPath($Destination).TrimEnd([char]'\\'),
    [System.IO.Path]::GetFullPath('C:\\GuideMe').TrimEnd([char]'\\'),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Isolated publish cannot target the active C:\\GuideMe installation.'
}

if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
    throw "App project not found: $appProject"
}

& dotnet publish $appProject -c Release -r win-x64 --self-contained false -o $Destination
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    throw "GuideMe publish failed with exit code $publishExitCode."
}

# The Chrome native messaging manifest points at GuideMe.NativeHost.exe
# in the same deployment directory. Publishing only the WPF app leaves
# a stale legacy data reader installed even when source builds pass.
if (-not (Test-Path -LiteralPath $nativeHostProject -PathType Leaf)) {
    throw "Native Host project not found: $nativeHostProject"
}
& dotnet publish $nativeHostProject -c Release -r win-x64 --self-contained false -o $Destination
$nativePublishExitCode = $LASTEXITCODE
if ($nativePublishExitCode -ne 0) {
    throw "GuideMe Native Host publish failed with exit code $nativePublishExitCode."
}
$nativeExecutable = Join-Path $Destination 'GuideMe.NativeHost.exe'
if (-not (Test-Path -LiteralPath $nativeExecutable -PathType Leaf)) {
    throw "GuideMe Native Host executable missing after publish: $nativeExecutable"
}

if (-not $SkipExtensionSync) {
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

}

if (-not (Test-Path -LiteralPath (Join-Path $Destination 'DAP.exe') -PathType Leaf)) {
    throw 'Published GuideMe executable is missing.'
}
if ($SkipExtensionSync) {
    Write-Host 'PASS: GuideMe and Native Host published to isolated output.'
} else {
    Write-Host 'PASS: GuideMe and Native Host published; Chrome extension icons synchronized.'
}
