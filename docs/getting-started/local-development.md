# Local Development

This page covers the current code-first development flow outside Docker.

## Build the App Projects

From the repository root:

```bash
dotnet build src/Common/LeanKernel.Logic/LeanKernel.Logic.csproj
dotnet build src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj
```

The active solution file is [`../../src/LeanKernel.sln`](../../src/LeanKernel.sln).

## Run the Gateway Directly

```bash
dotnet run --project src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
```

This is the most useful loop for endpoint and integration work.

## Default Non-Docker Behavior

Without environment overrides, the gateway reads:

- `src/Services/LeanKernel.Gateway/appsettings.json`
- `src/Services/LeanKernel.Gateway/appsettings.Development.json`

Those files default to SQLite for local persistence.

Code paths:

- [`../../src/Services/LeanKernel.Gateway/appsettings.json`](../../src/Services/LeanKernel.Gateway/appsettings.json)
- [`../../src/Services/LeanKernel.Gateway/appsettings.Development.json`](../../src/Services/LeanKernel.Gateway/appsettings.Development.json)
- [`../../src/Common/LeanKernel.Data/Extensions/IConfigurationExtensions.cs`](../../src/Common/LeanKernel.Data/Extensions/IConfigurationExtensions.cs)
- [`../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs`](../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs)

## Docker Override Behavior

Inside Docker Compose, the gateway is configured with `ConnectionStrings__Postgres`, so runtime persistence uses PostgreSQL instead of SQLite.

Reference: [`../../docker-compose.yml`](../../docker-compose.yml)

## Suggested Reading Order

1. [Solution structure](../architecture/solution-structure.md)
2. [Runtime flows](../architecture/runtime-flows.md)
3. [Build and test](../development/build-and-test.md)
