#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RESULTS_DIR="$ROOT_DIR/coverage-results"
THRESHOLD="${COVERAGE_THRESHOLD:-80}"

rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"

dotnet test "$ROOT_DIR/test/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj" \
  -c Release \
  --collect:"XPlat Code Coverage" \
  --settings "$ROOT_DIR/test/LeanKernel.Tests.Unit/coverage.runsettings" \
  --results-directory "$RESULTS_DIR"

"$ROOT_DIR/scripts/quality/coverage-gate.py" \
  --threshold "$THRESHOLD" \
  "$RESULTS_DIR/**/coverage.cobertura.xml"
