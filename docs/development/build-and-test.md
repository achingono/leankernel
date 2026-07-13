# Build and Test

This page documents the current developer commands for the rebuild.

## Solution Build

From the repository root:

```bash
dotnet build src/LeanKernel.sln
```

## Focused App Builds

```bash
dotnet build src/Common/LeanKernel.Logic/LeanKernel.Logic.csproj
dotnet build src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj
```

## Test Projects

Current test projects:

- `test/LeanKernel.Tests.Unit`
- `test/LeanKernel.Tests.Integration`
- `test/LeanKernel.Tests.Playwright`

## Coverage Gate

```bash
scripts/quality/test-coverage.sh
```

Reference: [`../../scripts/quality/test-coverage.sh`](../../scripts/quality/test-coverage.sh)

## Sonar Workflows

Supporting quality scripts live under:

- `scripts/quality/sonarqube-scan.sh`
- `scripts/quality/sonarqube-summary.sh`
- `scripts/quality/sonarqube-create-user.sh`

## Current Note About Full Solution Verification

The app projects build cleanly. If full-solution verification fails, inspect the unit test project first for stale namespace references before assuming the runtime projects are broken.
