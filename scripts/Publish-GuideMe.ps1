param(
    [string]$Destination = 'C:\GuideMe',
    [switch]$SkipExtensionSync,
    [string]$ExtensionId
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\GuideMe.App\GuideMe.App.csproj'
$nativeHostProject = Join-Path $root 'src\GuideMe.Runtime.Web.NativeHost\GuideMe.Runtime.Web.NativeHost.csproj'
$extensionSource = Join-Path $root 'src\GuideMe.Learner.ChromeExtension'
$extensionDestination = Join-Path $Destination 'ChromeExtension'

$destinationCanonical = [System.IO.Path]::GetFullPath($Destination).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$activeCanonical = [System.IO.Path]::GetFullPath('C:\GuideMe').TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if ($SkipExtensionSync -and [string]::Equals(
    $destinationCanonical, $activeCanonical, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Isolated publish cannot target the active C:\GuideMe installation.'
}

# Fail fast before publishing to an active installation. A running app or
# Chrome native messaging host can lock DLLs in the deployment directory.
# Do not terminate processes automatically: the operator should close them.
if ([string]::Equals($destinationCanonical, $activeCanonical,
    [StringComparison]::OrdinalIgnoreCase)) {
    $runningGuideMe = @(Get-Process -Name 'GuideMe', 'GuideMe.NativeHost' -ErrorAction SilentlyContinue |
        Where-Object {
            $executablePath = $_.Path
            if ([string]::IsNullOrWhiteSpace($executablePath)) {
                # Cannot safely rule out an active installed process.
                return $true
            }
            $executablePath.StartsWith(
                $activeCanonical + [System.IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)
        })
    if ($runningGuideMe.Count -gt 0) {
        $details = ($runningGuideMe | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join ', '
        throw "GuideMe publish blocked: running processes lock C:\GuideMe files: $details. Close GuideMe and Chrome windows using the GuideMe extension, then retry. No files were published."
    }
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
# in the NativeHost subdirectory. Publishing only the WPF app leaves
# a stale legacy data reader installed even when source builds pass.
if (-not (Test-Path -LiteralPath $nativeHostProject -PathType Leaf)) {
    throw "Native Host project not found: $nativeHostProject"
}
$nativeHostDestination = Join-Path $Destination 'NativeHost'
& dotnet publish $nativeHostProject -c Release -r win-x64 --self-contained false -o $nativeHostDestination
$nativePublishExitCode = $LASTEXITCODE
if ($nativePublishExitCode -ne 0) {
    throw "GuideMe Native Host publish failed with exit code $nativePublishExitCode."
}
$nativeExecutable = Join-Path $nativeHostDestination 'GuideMe.NativeHost.exe'
if (-not (Test-Path -LiteralPath $nativeExecutable -PathType Leaf)) {
    throw "GuideMe Native Host executable missing after publish: $nativeExecutable"
}

if (-not $SkipExtensionSync) {
    if (-not (Test-Path -LiteralPath $extensionDestination -PathType Container)) {
        throw "Chrome extension deployment directory not found: $extensionDestination. Existing extension files were not modified."
    }

    # Deploy every extension asset, not just icons. Documentation and Git
    # placeholder files are not part of the Chrome extension package.
    $extensionFiles = @(Get-ChildItem -LiteralPath $extensionSource -File -Recurse |
        Where-Object { $_.Name -notin @('README.md', '.gitkeep') })
    foreach ($file in $extensionFiles) {
        $relativePath = $file.FullName.Substring($extensionSource.Length).TrimStart('\')
        $target = Join-Path $extensionDestination $relativePath
        $targetDirectory = Split-Path -Parent $target
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
            throw "Chrome extension verification failed: $relativePath"
        }
    }
    Write-Host "PASS: $($extensionFiles.Count) Chrome extension files synchronized and SHA256-verified."

}

if (-not (Test-Path -LiteralPath (Join-Path $Destination 'GuideMe.exe') -PathType Leaf)) {
    throw 'Published GuideMe executable is missing.'
}
# Native Messaging registration is part of a successful production publish.
# Never infer an extension ID from the Instructor extension or a different profile.
# Reuse a previously verified Learner manifest, or require the ID explicitly
# on first installation / after a missing manifest.
if ([string]::Equals($destinationCanonical, $activeCanonical,
    [StringComparison]::OrdinalIgnoreCase)) {
    $manifestPath = Join-Path $env:LOCALAPPDATA 'GuideMe\NativeMessaging\com.guideme.web_runtime.json'
    if ([string]::IsNullOrWhiteSpace($ExtensionId)) {
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            try {
                $existing = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
                $origins = @($existing.allowed_origins)
                if ($existing.name -eq 'com.guideme.web_runtime' -and
                    $origins.Count -eq 1 -and
                    $origins[0] -match '^chrome-extension://([a-p]{32})/
) {
                    $ExtensionId = $Matches[1]
                }
            } catch {
                Write-Warning "Existing Learner Native Messaging manifest is invalid: $($_.Exception.Message)"
            }
        }
    }
    if ($ExtensionId -notmatch '^[a-p]{32}
) {
        throw 'Learner Chrome extension ID is unknown. Run Publish-GuideMe.ps1 -ExtensionId <32-character ID from chrome://extensions> once. The ID will be reused from the registered manifest on later publishes.'
    }

    & (Join-Path $PSScriptRoot 'Register-WebNativeHost.ps1') -PackagePath $Destination -ExtensionId $ExtensionId
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Learner Native Messaging manifest missing after registration: $manifestPath"
    }
    $registered = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $registryPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.guideme.web_runtime'
    $registeredManifest = if (Test-Path -LiteralPath $registryPath) {
        (Get-Item -LiteralPath $registryPath).GetValue('')
    } else { $null }
    if ($registered.name -ne 'com.guideme.web_runtime' -or
        $registered.path -ne $nativeExecutable -or
        -not (Test-Path -LiteralPath $registered.path -PathType Leaf) -or
        @($registered.allowed_origins).Count -ne 1 -or
        ($registered.allowed_origins | Select-Object -First 1) -ne "chrome-extension://$ExtensionId/" -or
        $registeredManifest -ne $manifestPath) {
        throw 'Learner Native Messaging registration verification failed.'
    }
    Write-Host 'PASS: Learner Native Messaging manifest, executable, origin and Chrome registry verified.'
}

if ($SkipExtensionSync) {
    Write-Host 'PASS: GuideMe and Native Host published to isolated output.'
} else {
    Write-Host 'PASS: GuideMe and Native Host published; Chrome extension files synchronized.'
}
