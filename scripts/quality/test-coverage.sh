#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RESULTS_DIR="$ROOT_DIR/coverage-results"
THRESHOLD="${COVERAGE_THRESHOLD:-80}"

rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"

dotnet test "$ROOT_DIR/src/LeanKernel.sln" \
  -c Release \
  --collect:"XPlat Code Coverage" \
  --filter "FullyQualifiedName!~Integration" \
  --settings "$ROOT_DIR/src/LeanKernel.Tests.Unit/coverage.runsettings" \
  --results-directory "$RESULTS_DIR"

"$ROOT_DIR/scripts/quality/coverage-gate.py" \
  --threshold "$THRESHOLD" \
  "$RESULTS_DIR/**/coverage.cobertura.xml"
