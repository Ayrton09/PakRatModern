param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

function Convert-ToWslPath {
    param([string]$Value)

    if ($Value -match '^[A-Za-z]:\\') {
        $drive = $Value.Substring(0, 1).ToLowerInvariant()
        $tail = $Value.Substring(2) -replace '\\', '/'
        return "/mnt/$drive$tail"
    }

    return ($Value -replace '\\', '/')
}

# pakrat_modern.py necesita Python 3.8 o posterior.
$versionCheck = 'import sys; raise SystemExit(0 if sys.version_info >= (3, 8) else 1)'

function Test-Python3Candidate {
    param(
        [string]$Command,
        [string[]]$PrefixArgs = @()
    )

    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) { return $false }

    try {
        & $Command @PrefixArgs -c $versionCheck > $null 2> $null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

if (-not $Rest -or $Rest.Count -eq 0) {
    Write-Host "Usage: .\pakrat_modern.ps1 <command> [args]"
    Write-Host "Example: .\pakrat_modern.ps1 list C:\maps\test.bsp"
    exit 1
}

$scriptPath = Join-Path $PSScriptRoot 'pakrat_modern.py'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    Write-Error "pakrat_modern.py was not found next to this wrapper: $scriptPath"
    exit 1
}

$candidates = @(
    @{ Command = 'py'; Args = @('-3') },
    @{ Command = 'python3'; Args = @() },
    @{ Command = 'python'; Args = @() }
)

foreach ($candidate in $candidates) {
    $command = [string]$candidate.Command
    $prefixArgs = [string[]]$candidate.Args
    if (Test-Python3Candidate -Command $command -PrefixArgs $prefixArgs) {
        & $command @prefixArgs $scriptPath @Rest
        exit $LASTEXITCODE
    }
}

if (Get-Command wsl.exe -ErrorAction SilentlyContinue) {
    & wsl.exe -e python3 -c $versionCheck > $null 2> $null
    if ($LASTEXITCODE -eq 0) {
        $converted = New-Object System.Collections.Generic.List[string]
        foreach ($arg in $Rest) {
            [void]$converted.Add((Convert-ToWslPath -Value $arg))
        }

        # --cd en el directorio actual: una ruta relativa se resuelve donde la
        # escribio el usuario, no junto al script. -e ejecuta python3 sin pasar
        # por el shell de Linux, que expandiria comodines y variables.
        & wsl.exe --cd (Get-Location).ProviderPath -e python3 (Convert-ToWslPath -Value $scriptPath) @($converted.ToArray())
        exit $LASTEXITCODE
    }
}

Write-Error 'Python 3.8 or later was not found (neither on Windows nor in WSL). Install Python or use the GUI.'
exit 1
