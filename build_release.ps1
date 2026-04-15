param(
    [string]$ReleaseRoot = "$PSScriptRoot\release\PakRatModern",
    [string]$ZipPath = "$PSScriptRoot\release\PakRatModern-release.zip"
)

$ErrorActionPreference = 'Stop'

if (Test-Path $ReleaseRoot) {
    Remove-Item $ReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ReleaseRoot 'assets') | Out-Null

Copy-Item "$PSScriptRoot\PakRatModern.exe" $ReleaseRoot -Force
if (Test-Path "$PSScriptRoot\PakRatModern.exe.config") {
    Copy-Item "$PSScriptRoot\PakRatModern.exe.config" $ReleaseRoot -Force
}
Copy-Item "$PSScriptRoot\pakrat_modern_gui.bat" $ReleaseRoot -Force
Copy-Item "$PSScriptRoot\pakrat_modern_gui.ps1" $ReleaseRoot -Force
Copy-Item "$PSScriptRoot\README.md" $ReleaseRoot -Force
Copy-Item "$PSScriptRoot\assets\pakrat_modern.ico" (Join-Path $ReleaseRoot 'assets\pakrat_modern.ico') -Force
Copy-Item "$PSScriptRoot\assets\pakrat_modern_icon.png" (Join-Path $ReleaseRoot 'assets\pakrat_modern_icon.png') -Force

$shortcutPath = Join-Path $ReleaseRoot 'PakRat Modern.lnk'
$wshell = New-Object -ComObject WScript.Shell
$shortcut = $wshell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $ReleaseRoot 'PakRatModern.exe'
$shortcut.WorkingDirectory = $ReleaseRoot
$shortcut.IconLocation = (Join-Path $ReleaseRoot 'assets\pakrat_modern.ico')
$shortcut.Description = 'Launch PakRat Modern'
$shortcut.Save()

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
Compress-Archive -Path (Join-Path $ReleaseRoot '*') -DestinationPath $ZipPath

Write-Output $ReleaseRoot
Write-Output $ZipPath
