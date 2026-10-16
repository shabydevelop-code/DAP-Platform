# One-time repository-wide source rename. Run from a clean checkout on Windows.
# Does not rename the GitHub repository, change the V5 schema, or delete data.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$changes = @(git status --porcelain)
if ($LASTEXITCODE -ne 0 -or $changes.Count -gt 0) {
    throw 'Working tree must be clean before the rename.'
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
$files = @(git ls-files)
foreach ($relative in $files) {
    if ($relative -match '\.(png|ico)$' -or $relative -match '\.sql$') { continue }
    $path = Join-Path $root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $old = [IO.File]::ReadAllText($path)
    $new = $old.Replace('DAP', 'GuideMe').Replace('Dap', 'GuideMe')
    $new = [regex]::Replace($new, '(?<![A-Za-z])dap(?=[A-Z._-])', 'guideMe')
    $new = [regex]::Replace($new, '(?<![A-Za-z])dap\b', 'guideme')
    # The repository URL/path is not renamed as part of this source migration.
    $new = $new.Replace('GuideMe-Platform', 'DAP-Platform')
    if ($new -cne $old) {
        [IO.File]::WriteAllText($path, $new, $utf8)
    }
}

# Rename project directories in a single coordinated pass.
$dirs = @(Get-ChildItem (Join-Path $root 'src') -Directory |
    Where-Object { $_.Name -like 'DAP.*' })
foreach ($dir in $dirs) {
    $newName = $dir.Name.Replace('DAP.', 'GuideMe.')
    & git mv -- $dir.FullName (Join-Path $dir.Parent.FullName $newName)
    if ($LASTEXITCODE -ne 0) { throw "Failed to rename directory: $($dir.Name)" }
}

# Rename project files and legacy class files (including demo projects).
$renameFiles = @(Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'demos') -Recurse -File |
    Where-Object { $_.Name -match 'DAP|Dap' })
foreach ($file in $renameFiles) {
    $newName = $file.Name.Replace('DAP', 'GuideMe').Replace('Dap', 'GuideMe')
    & git mv -- $file.FullName (Join-Path $file.DirectoryName $newName)
    if ($LASTEXITCODE -ne 0) { throw "Failed to rename file: $($file.FullName)" }
}

Write-Host 'GuideMe source migration applied locally.'
Write-Host 'Review the changes and run the build and runtime regressions before committing.'
Write-Host 'The Chrome Native Messaging host name has changed; re-register the host and reload the extension.'
git status --short
