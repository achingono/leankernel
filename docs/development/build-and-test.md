# Build and Test

Use this workflow to match CI behavior locally.

## CI-Aligned Commands

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal --filter 'FullyQualifiedName!~Playwright' --logger "trx;LogFileName=results.trx" --collect:"XPlat Code Coverage"
scripts/quality/test-coverage.sh
```

## Playwright Commands

```bash
npx playwright install --with-deps chromium
dotnet run --project src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
LEANKERNEL_BASE_URL="http://127.0.0.1:5080" dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj
```

## Related Pages

- [Development index](index.md)
- [Quality](quality.md)
- [Getting started testing](../getting-started/local-testing.md)

## Source References

- `.github/workflows/build-and-test.yml`
- `AGENTS.md`
