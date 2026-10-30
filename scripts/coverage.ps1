# Roda os testes e mostra a cobertura (PROJECT_STANDARDS, secao 14).
# A cobertura ja vem ligada em todo `dotnet test` (Directory.Build.props); este script so junta
# os arquivos de cada projeto num relatorio: resumo no terminal e HTML em
# artifacts/coverage/report/index.html.
#
#   ./scripts/coverage.ps1             tudo (precisa do Docker para a integracao)
#   ./scripts/coverage.ps1 -SemDocker  so unidade e arquitetura
param([switch]$SemDocker)

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Remove-Item -Recurse -Force artifacts/coverage -ErrorAction SilentlyContinue

    dotnet tool restore | Out-Null

    $testArgs = @()
    if ($SemDocker) { $testArgs = @('--filter-not-namespace', '*.IntegrationTests*', '--ignore-exit-code', '8') }

    dotnet test @testArgs
    $testExit = $LASTEXITCODE

    dotnet reportgenerator '-reports:artifacts/coverage/*.cobertura.xml' '-targetdir:artifacts/coverage/report' '-reporttypes:Html;TextSummary' '-filefilters:-*.g.cs' '-verbosity:Warning'

    # Resumo geral e uma linha por assembly (as classes, recuadas, ficam no HTML).
    $lines = Get-Content artifacts/coverage/report/Summary.txt
    $blank = [array]::IndexOf($lines, '')
    $lines[0..$blank] + ($lines[$blank..($lines.Length - 1)] | Where-Object { $_ -and $_ -notmatch '^\s' }) |
        ForEach-Object { Write-Host $_ }
    Write-Host ''
    Write-Host "Relatorio completo: $(Join-Path $root 'artifacts/coverage/report/index.html')"

    exit $testExit
}
finally {
    Pop-Location
}
