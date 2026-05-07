# Development

This section contains guides for developing, testing, and maintaining the LeanKernel platform.

## Contents

| Document | Description |
|----------|-------------|
| [quality.md](quality.md) | How to run the quality gates: unit tests, coverage enforcement, local SonarQube scan, and Docker image validation. |
| [litellm-spec.md](litellm-spec.md) | Plan and design for replacing the hand-expanded LiteLLM config with a single-file source spec compiled at container startup. |

## Common Commands

```bash
# Build and test
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln -c Release --no-restore
dotnet test src/LeanKernel.sln -c Release --no-build

# Coverage gate
scripts/quality/test-coverage.sh

# Local SonarQube scan
scripts/quality/sonarqube-scan.sh

# Docker build
docker compose build
docker compose up -d
```
