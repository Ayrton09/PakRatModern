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

function Test-Python3Candidate {
    param(
        [string]$Command,
        [string[]]$PrefixArgs = @()
    )

    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) { return $false }

    try {
        & $Command @PrefixArgs -c "import sys; raise SystemExit(0 if sys.version_info[0] >= 3 else 1)" > $null 2> $null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

if (-not $Rest -or $Rest.Count -eq 0) {
    Write-Host "Uso: .\pakrat_modern.ps1 <comando> [args]"
    Write-Host "Ejemplo: .\pakrat_modern.ps1 list C:\maps\test.bsp"
    exit 1
}

$scriptPath = Join-Path $PSScriptRoot 'pakrat_modern.py'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    Write-Error "No se encontro pakrat_modern.py junto a este wrapper: $scriptPath"
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
    $converted = New-Object System.Collections.Generic.List[string]
    foreach ($arg in $Rest) {
        [void]$converted.Add((Convert-ToWslPath -Value $arg))
    }
    & wsl.exe --cd (Convert-ToWslPath -Value $PSScriptRoot) python3 (Convert-ToWslPath -Value $scriptPath) @($converted.ToArray())
    exit $LASTEXITCODE
}

Write-Error 'No se encontro Python 3 local ni WSL con python3. Instala Python 3 o ejecuta la GUI.'
exit 1
