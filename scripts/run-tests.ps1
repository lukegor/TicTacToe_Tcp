param(
    [int]$Threshold = 80,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force artifacts | Out-Null

dotnet test tests/TicTacToe.Tests -c $Configuration `
    --coverage --coverage-output-format cobertura --coverage-output "$PWD/artifacts/core.cobertura.xml"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test tests/TicTacToe.UiTests -c $Configuration `
    --coverage --coverage-output-format cobertura --coverage-output "$PWD/artifacts/ui.cobertura.xml"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet tool run reportgenerator -- "-reports:$PWD/artifacts/core.cobertura.xml;$PWD/artifacts/ui.cobertura.xml" `
    "-targetdir:$PWD/artifacts/coveragereport" "-reporttypes:TextSummary"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$summary = Get-Content artifacts/coveragereport/Summary.txt -Raw
Write-Output $summary

$lineRate = [double]([regex]::Match($summary, 'Line coverage: ([\d.,]+)%').Groups[1].Value.Replace(',', '.'))

if ($lineRate -lt $Threshold) {
    Write-Output "Coverage gate FAILED: $lineRate% < required $Threshold%"
    exit 1
}

Write-Output "Coverage gate passed: $lineRate% >= $Threshold%"
