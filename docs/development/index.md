# Development

This section contains guides for building, testing, and operating LeanKernel from source.

## Contents

| Document | Description |
|----------|-------------|
| [quality.md](quality.md) | Quality gates: build/test, coverage enforcement, local SonarQube scan, and image validation workflow. |
| [litellm-spec.md](litellm-spec.md) | Current LiteLLM source-spec compiler contract (`config/litellm/config.yaml` -> rendered runtime config). |

## Common Commands

```bash
# Build and test
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal

# Coverage gate
scripts/quality/test-coverage.sh

# Local SonarQube scan (uses docker-compose.sonar.yml)
scripts/quality/sonarqube-scan.sh

# Docker validation / startup
docker compose config
docker build -t leankernel-engine:local .
docker compose up -d
```
