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

    return $Value
}

if (-not $Rest -or $Rest.Count -eq 0) {
    Write-Host "Uso: .\\pakrat_modern.ps1 <comando> [args]"
    Write-Host "Ejemplo: .\\pakrat_modern.ps1 list C:\\maps\\test.bsp"
    exit 1
}

$scriptPath = '/mnt/c/Users/Ayrton/Documents/New project 7/pakrat_modern.py'
$converted = @()
foreach ($arg in $Rest) {
    $converted += Convert-ToWslPath -Value $arg
}

& wsl.exe python3 $scriptPath @converted
exit $LASTEXITCODE