param(
    [string]$ReleaseRoot = "$PSScriptRoot\release\PakRatModern",
    [string]$ZipPath = "$PSScriptRoot\release\PakRatModern-release.zip"
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$releaseBase = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'release'))
$resolvedReleaseRoot = [System.IO.Path]::GetFullPath($ReleaseRoot)
$resolvedZipPath = [System.IO.Path]::GetFullPath($ZipPath)
$releaseBasePrefix = $releaseBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if ($resolvedReleaseRoot -ne $releaseBase -and -not $resolvedReleaseRoot.StartsWith($releaseBasePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ReleaseRoot must be inside the project release folder: $releaseBase"
}

if ($resolvedZipPath -ne $releaseBase -and -not $resolvedZipPath.StartsWith($releaseBasePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ZipPath must be inside the project release folder: $releaseBase"
}

$exePath = Join-Path $projectRoot 'PakRatModern.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Missing PakRatModern.exe. Run build_pakrat_modern_gui.ps1 first."
}

if (-not (Test-Path -LiteralPath $releaseBase -PathType Container)) {
    New-Item -ItemType Directory -Path $releaseBase | Out-Null
}

if (Test-Path -LiteralPath $resolvedReleaseRoot) {
    Remove-Item -LiteralPath $resolvedReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedReleaseRoot | Out-Null

Copy-Item -LiteralPath $exePath -Destination $resolvedReleaseRoot -Force
$configPath = Join-Path $projectRoot 'PakRatModern.exe.config'
if (Test-Path -LiteralPath $configPath -PathType Leaf) {
    Copy-Item -LiteralPath $configPath -Destination $resolvedReleaseRoot -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'pakrat_modern_gui.bat') -Destination $resolvedReleaseRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'pakrat_modern_gui.ps1') -Destination $resolvedReleaseRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'pakrat_modern.ps1') -Destination $resolvedReleaseRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'pakrat_modern.py') -Destination $resolvedReleaseRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $resolvedReleaseRoot -Force
if (Test-Path -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -PathType Leaf) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $resolvedReleaseRoot -Force
}
foreach ($notesFile in (Get-ChildItem -LiteralPath $projectRoot -Filter 'GITHUB_RELEASE_NOTES_*.md' -File -ErrorAction SilentlyContinue)) {
    Copy-Item -LiteralPath $notesFile.FullName -Destination $resolvedReleaseRoot -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'pakrat_modern.ico') -Destination $resolvedReleaseRoot -Force

$shortcutPath = Join-Path $resolvedReleaseRoot 'PakRat Modern.lnk'
$wshell = New-Object -ComObject WScript.Shell
$shortcut = $wshell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $resolvedReleaseRoot 'PakRatModern.exe'
$shortcut.WorkingDirectory = $resolvedReleaseRoot
$shortcut.IconLocation = (Join-Path $resolvedReleaseRoot 'pakrat_modern.ico')
$shortcut.Description = 'Launch PakRat Modern'
$shortcut.Save()

if (Test-Path -LiteralPath $resolvedZipPath) {
    Remove-Item -LiteralPath $resolvedZipPath -Force
}
Compress-Archive -Path (Join-Path $resolvedReleaseRoot '*') -DestinationPath $resolvedZipPath

Write-Output $resolvedReleaseRoot
Write-Output $resolvedZipPath
