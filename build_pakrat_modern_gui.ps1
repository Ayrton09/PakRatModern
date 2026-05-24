param(
    [string]$InputFile = "$PSScriptRoot\\pakrat_modern_gui.ps1",
    [string]$OutputFile = "$PSScriptRoot\\PakRatModern.exe",
    [string]$IconFile = "$PSScriptRoot\\pakrat_modern.ico",
    [string]$Version = '1.2.1.0'
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name ps2exe)) {
    throw "Missing module 'ps2exe'. Install it first with: Install-Module ps2exe -Scope CurrentUser"
}

Import-Module ps2exe

$invokeParams = @{
    inputFile = $InputFile
    outputFile = $OutputFile
    noConsole = $true
    STA = $true
    title = 'PakRat Modern'
    product = 'PakRat Modern'
    company = 'Ayrton09'
    description = 'Modern Source BSP PAK editor inspired by PakRat'
    version = $Version
    configFile = $true
    supportOS = $true
    winFormsDPIAware = $true
    longPaths = $true
}

if (Test-Path -LiteralPath $IconFile -PathType Leaf) {
    $invokeParams.iconFile = $IconFile
}

Invoke-ps2exe @invokeParams
