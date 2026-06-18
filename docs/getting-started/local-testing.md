# Local Testing

These commands mirror CI behavior.

## Build and Unit/Integration Tests

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal --filter 'FullyQualifiedName!~Playwright' --logger "trx;LogFileName=results.trx" --collect:"XPlat Code Coverage"
scripts/quality/test-coverage.sh
```

## Playwright Tests

Install browser dependencies:

```bash
npx playwright install --with-deps chromium
```

Run app and tests:

```bash
dotnet run --project src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
LEANKERNEL_BASE_URL="http://127.0.0.1:5080" dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj
```

## Related Pages

- [Development index](../development/index.md)
- [Build and test guide](../development/build-and-test.md)

## Source References

- `.github/workflows/build-and-test.yml`
- `test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj`
