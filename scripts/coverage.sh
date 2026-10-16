#!/usr/bin/env bash
# Roda os testes e mostra a cobertura (PROJECT_STANDARDS, secao 14).
# A cobertura ja vem ligada em todo `dotnet test` (Directory.Build.props); este script so junta
# os arquivos de cada projeto num relatorio: resumo no terminal e HTML em
# artifacts/coverage/report/index.html.
#
#   ./scripts/coverage.sh              tudo (precisa do Docker para a integracao)
#   ./scripts/coverage.sh --sem-docker so unidade e arquitetura
set -u

cd "$(dirname "$0")/.."
rm -rf artifacts/coverage

dotnet tool restore > /dev/null

test_args=()
if [ "${1:-}" = "--sem-docker" ]; then
  test_args=(--filter-not-namespace "*.IntegrationTests*" --ignore-exit-code 8)
fi

dotnet test "${test_args[@]}"
test_exit=$?

dotnet reportgenerator "-reports:artifacts/coverage/*.cobertura.xml" "-targetdir:artifacts/coverage/report" "-reporttypes:Html;TextSummary" "-filefilters:-*.g.cs" "-verbosity:Warning"

# Resumo geral e uma linha por assembly (as classes, recuadas, ficam no HTML).
awk '!seen && /^$/ { seen = 1; print; next } !seen || /^[^ ]/' artifacts/coverage/report/Summary.txt
echo
echo "Relatorio completo: $(pwd)/artifacts/coverage/report/index.html"

exit $test_exit
