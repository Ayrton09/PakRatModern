#requires -Version 5.1
<#
.SYNOPSIS
    Compila, prueba y empaqueta PakRat Modern.

.DESCRIPTION
    Desde 1.3.0 la aplicacion es un ejecutable .NET nativo compilado con el SDK,
    no un script de PowerShell empaquetado con ps2exe. El binario resultante no
    contiene un script embebido ni hospeda PowerShell, que era lo que disparaba
    las detecciones heuristicas de los antivirus.

    Las pruebas se corren siempre antes de empaquetar: un release que no pasa la
    suite no deberia poder armarse.
#>
param(
    [string]$ReleaseRoot = "$PSScriptRoot\release\PakRatModern",
    [string]$ZipPath = "$PSScriptRoot\release\PakRatModern-release.zip",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$releaseBase = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'release'))
$resolvedReleaseRoot = [System.IO.Path]::GetFullPath($ReleaseRoot)
$resolvedZipPath = [System.IO.Path]::GetFullPath($ZipPath)

# Las rutas de salida quedan confinadas a release\ para que un parametro mal
# escrito no borre un directorio arbitrario mas abajo (se usa Remove-Item -Recurse).
$releaseBasePrefix = $releaseBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if ($resolvedReleaseRoot -ne $releaseBase -and -not $resolvedReleaseRoot.StartsWith($releaseBasePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ReleaseRoot must be inside the project release folder: $releaseBase"
}

if ($resolvedZipPath -ne $releaseBase -and -not $resolvedZipPath.StartsWith($releaseBasePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ZipPath must be inside the project release folder: $releaseBase"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "Missing the .NET SDK. Install it from https://dotnet.microsoft.com/download"
}

$appProject = Join-Path $projectRoot 'src\PakRatModern.App\PakRatModern.App.csproj'
$testProject = Join-Path $projectRoot 'src\PakRatModern.Tests\PakRatModern.Tests.csproj'

# Un solo lugar define el target de la aplicacion: las pruebas y el directorio de
# salida se derivan de el, para que cambiarlo no deje rutas apuntando a carpetas
# que ya no existen.
$appXml = [xml](Get-Content -Raw -LiteralPath $appProject)

$appTargetFramework = $appXml.Project.PropertyGroup.TargetFramework |
    Where-Object { $_ } | Select-Object -First 1
if (-not $appTargetFramework) { throw "Could not read TargetFramework from $appProject" }

$appVersion = $appXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $appVersion) { throw "Could not read Version from $appProject" }

# --- Pruebas ---------------------------------------------------------------

if (-not $SkipTests) {
    Write-Host 'Running test suite...' -ForegroundColor Cyan

    # El proyecto de pruebas apunta a varios frameworks (Windows y multiplataforma),
    # asi que hay que elegir uno explicitamente: aca se usa el mismo que la aplicacion.
    & dotnet run --project $testProject -c Release -f $appTargetFramework -v q --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Test suite failed with exit code $LASTEXITCODE. Release aborted."
    }
}

# --- Compilacion -----------------------------------------------------------

Write-Host 'Publishing application...' -ForegroundColor Cyan
$publishDir = Join-Path $projectRoot "src\PakRatModern.App\bin\Release\$appTargetFramework\publish"

& dotnet publish $appProject -c Release -f $appTargetFramework -v q --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $publishDir 'PakRatModern.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Expected output not found: $exePath"
}

# --- Empaquetado -----------------------------------------------------------

if (-not (Test-Path -LiteralPath $releaseBase -PathType Container)) {
    New-Item -ItemType Directory -Path $releaseBase | Out-Null
}

if (Test-Path -LiteralPath $resolvedReleaseRoot) {
    Remove-Item -LiteralPath $resolvedReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedReleaseRoot | Out-Null

# Binarios de la aplicacion
foreach ($pattern in 'PakRatModern.exe', 'PakRatModern.exe.config', '*.dll') {
    Get-ChildItem -LiteralPath $publishDir -Filter $pattern -File -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $resolvedReleaseRoot -Force }
}

# CLI, documentacion e icono
$extras = @(
    'pakrat_modern.py',
    'pakrat_modern.ps1',
    'pakrat_modern.ico',
    'README.md',
    'LICENSE'
)

foreach ($name in $extras) {
    $source = Join-Path $projectRoot $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination $resolvedReleaseRoot -Force
    }
}

# Solo las notas de esta version: incluir las anteriores dentro del zip es ruido
# y confunde sobre que se esta descargando.
$notesPath = Join-Path $projectRoot "GITHUB_RELEASE_NOTES_$appVersion.md"
if (Test-Path -LiteralPath $notesPath -PathType Leaf) {
    Copy-Item -LiteralPath $notesPath -Destination $resolvedReleaseRoot -Force
} else {
    Write-Warning "No release notes found for $appVersion ($notesPath)"
}

# Acceso directo
$shortcutPath = Join-Path $resolvedReleaseRoot 'PakRat Modern.lnk'
$wshell = New-Object -ComObject WScript.Shell
$shortcut = $wshell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $resolvedReleaseRoot 'PakRatModern.exe'
$shortcut.WorkingDirectory = $resolvedReleaseRoot
$shortcut.IconLocation = Join-Path $resolvedReleaseRoot 'pakrat_modern.ico'
$shortcut.Description = 'Launch PakRat Modern'
$shortcut.Save()

if (Test-Path -LiteralPath $resolvedZipPath) {
    Remove-Item -LiteralPath $resolvedZipPath -Force
}
Compress-Archive -Path (Join-Path $resolvedReleaseRoot '*') -DestinationPath $resolvedZipPath

# --- Hashes ----------------------------------------------------------------
# Se publican junto al release para que cualquiera pueda verificar que el
# binario descargado es el mismo que se compilo aca.

Write-Host ''
Write-Host 'SHA256:' -ForegroundColor Cyan
foreach ($target in @((Join-Path $resolvedReleaseRoot 'PakRatModern.exe'), $resolvedZipPath)) {
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("  {0}  {1}" -f $hash, [System.IO.Path]::GetFileName($target))
}

Write-Host ''
Write-Output $resolvedReleaseRoot
Write-Output $resolvedZipPath
