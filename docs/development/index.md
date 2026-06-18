# Development

This section contains guides for building, testing, and operating LeanKernel from source.

## Contents

| Document | Description |
|----------|-------------|
| [build-and-test.md](build-and-test.md) | CI-aligned local build, test, and Playwright execution workflow. |
| [quality-gates.md](quality-gates.md) | Canonical entry for quality gates and verification workflows. |
| [quality.md](quality.md) | Detailed quality workflow reference. |
| [docs-style-guide.md](docs-style-guide.md) | Documentation structure, naming, and hyperlink conventions. |
| [docs-inventory-matrix.md](docs-inventory-matrix.md) | Current docs inventory status and canonical mapping matrix. |
| [litellm-spec.md](litellm-spec.md) | Current LiteLLM source-spec compiler contract (`config/litellm/config.yaml` -> rendered runtime config). |

## Common Commands

```bash
# Build and test
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal --filter 'FullyQualifiedName!~Playwright'

# Coverage gate
scripts/quality/test-coverage.sh

# Playwright prerequisites
npx playwright install --with-deps chromium

# Local SonarQube scan (uses docker-compose.sonar.yml)
scripts/quality/sonarqube-scan.sh

# Docker validation / startup
docker compose config
docker build -t leankernel-engine:local .
docker compose up -d
```
