param(
    [string]$Destination = 'C:\GuideMe\InstructorWeb',
    [string]$ExtensionDestination = 'C:\GuideMe\InstructorChromeExtension'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\GuideMe.Instructor.Web\GuideMe.Instructor.Web.csproj'
$source = Join-Path $root 'src\GuideMe.Instructor.ChromeExtension'
$hostProject = Join-Path $root 'src\GuideMe.Instructor.NativeHost\GuideMe.Instructor.NativeHost.csproj'

if (-not (Test-Path $project) -or -not (Test-Path $source)) {
    throw 'GuideMe Instructor Web sources are missing.'
}

# Publish only the target platform, without bundling the .NET runtime.
# Build in a fresh staging directory so stale cross-platform native assets from
# older publishes cannot survive in the delivered runtimes directory.
# A running Instructor can keep the old UI served from memory or lock
# deployment binaries. Stop it explicitly before replacing production files.
$installedExe = [IO.Path]::GetFullPath((Join-Path $Destination 'GuideMe.Instructor.Web.exe'))
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'GuideMe.Instructor.Web.exe'" |
    Where-Object { $_.ExecutablePath -and
        [string]::Equals($_.ExecutablePath, $installedExe,
            [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) {
    $pids = ($running | ForEach-Object { $_.ProcessId }) -join ', '
    throw "Instructor Web is still running from the production folder (PID: $pids). Close it before publishing."
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("GuideMe-Instructor-publish-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    dotnet publish $project -c Release -r win-x64 --self-contained false -o $staging
    if ($LASTEXITCODE -ne 0) {
        throw "Instructor Web win-x64 publish failed (exit $LASTEXITCODE). Existing deployment was not modified."
    }

    if (-not (Test-Path -LiteralPath (Join-Path $staging 'GuideMe.Instructor.Web.exe'))) {
        throw 'Instructor Web executable missing from staged publish. Existing deployment was not modified.'
    }

    # Guard against accidentally shipping the obsolete standalone recorder UI.
    $stagedHtml = Join-Path $staging 'wwwroot\index.html'
    $stagedJs = Join-Path $staging 'wwwroot\main.js'
    if (-not (Test-Path -LiteralPath $stagedHtml) -or
        -not (Test-Path -LiteralPath $stagedJs)) {
        throw 'Instructor Web UI files are missing from publish output.'
    }
    $htmlContent = [IO.File]::ReadAllText($stagedHtml)
    $jsContent = [IO.File]::ReadAllText($stagedJs)
    if ($htmlContent.Contains('capture-panel') -or
        $htmlContent.Contains('capture-pair') -or
        $jsContent.Contains("capture-pair")) {
        throw 'Publish blocked: obsolete global recording controls are still present in Web UI.'
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    # Only the dependency folder is replaced in full. Other deployment files
    # (including local configuration) are left intact.
    $oldRuntimes = Join-Path $Destination 'runtimes'
    $newRuntimes = Join-Path $staging 'runtimes'
    if (Test-Path -LiteralPath $oldRuntimes) {
        Remove-Item -LiteralPath $oldRuntimes -Recurse -Force
    }
    # Copy each file to its exact relative destination. Copying an existing
    # directory with Copy-Item -Recurse can nest wwwroot inside wwwroot,
    # leaving the old index.html in place.
    $stageRoot = [IO.Path]::GetFullPath($staging).TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File -Force) {
        $relativePath = $file.FullName.Substring($stageRoot.Length)
        $targetPath = Join-Path $Destination $relativePath
        $targetDirectory = Split-Path -Parent $targetPath
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
    }

    foreach ($asset in @('index.html', 'main.js', 'style.css')) {
        $from = Join-Path $staging "wwwroot\$asset"
        $to = Join-Path $Destination "wwwroot\$asset"
        if ((Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $to -Algorithm SHA256).Hash) {
            throw "Instructor Web UI sync verification failed: $asset"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

# Dedicated Instructor Native Host. Never overwrite Learner Native Messaging registration.
$hostDestination = Join-Path $Destination 'NativeHost'
dotnet publish $hostProject -c Release -r win-x64 --self-contained false -o $hostDestination
if ($LASTEXITCODE -ne 0) { throw "Instructor Native Host publish failed ($LASTEXITCODE)." }
$hostExe = Join-Path $hostDestination 'GuideMe.Instructor.NativeHost.exe'
if (-not (Test-Path -LiteralPath $hostExe)) { throw "Instructor Native Host not found: $hostExe" }
$extensionManifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
$keyBytes = [Convert]::FromBase64String($extensionManifest.key)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try { $hash = $sha256.ComputeHash($keyBytes) } finally { $sha256.Dispose() }
$extensionId = -join ($hash | Select-Object -First 16 | ForEach-Object {
    [char]([int][char]'a' + ($_ -shr 4)), [char]([int][char]'a' + ($_ -band 15))
})
$manifestDir = Join-Path $env:LOCALAPPDATA 'GuideMe\NativeMessaging'
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
$hostManifestPath = Join-Path $manifestDir 'com.guideme.instructor.json'
$hostManifest = [ordered]@{
    name = 'com.guideme.instructor'
    description = 'GuideMe Instructor Native Messaging Host'
    path = $hostExe
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}
[IO.File]::WriteAllText($hostManifestPath, ($hostManifest | ConvertTo-Json -Depth 4),
    [Text.UTF8Encoding]::new($false))
$registryPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.guideme.instructor'
New-Item -Path $registryPath -Force | Out-Null
Set-Item -Path $registryPath -Value $hostManifestPath
Write-Host "PASS: Instructor Native Messaging registered for Chrome extension $extensionId"

New-Item -ItemType Directory -Path $ExtensionDestination -Force | Out-Null
# These obsolete popup files are no longer part of the headless extension.
foreach ($obsolete in @('popup.html', 'popup.js')) {
    $oldPath = Join-Path $ExtensionDestination $obsolete
    if (Test-Path -LiteralPath $oldPath) { Remove-Item -LiteralPath $oldPath -Force }
}

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
# Materialize dedicated Instructor icons before Chrome reload.
& (Join-Path $PSScriptRoot 'Generate-GuideMeInstructorIcons.ps1') -Destination $ExtensionDestination
foreach ($size in @(16, 48, 128)) {
    $icon = Join-Path $ExtensionDestination "icon$size.png"
    if (-not (Test-Path -LiteralPath $icon) -or (Get-Item -LiteralPath $icon).Length -eq 0) {
        throw "Missing generated Instructor icon: $icon"
    }
}
Write-Host "PASS: Instructor Web published for win-x64 (framework-dependent) to $Destination"
Write-Host 'PASS: runtimes directory contains only dependencies from the fresh win-x64 publish.'
Write-Host "PASS: $($files.Count) Instructor extension files synchronized to $ExtensionDestination"
Write-Host 'Reload the unpacked GuideMe Instructor extension at chrome://extensions.'
Write-Host "Start Web Instructor: & `"$Destination\GuideMe.Instructor.Web.exe`""
