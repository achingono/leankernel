# Phase 1 Solution Scaffold PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Replace the legacy `src/` tree with a new .NET 10 domain-driven solution scaffold that all subsequent rearchitecture work can build on.
- **Plan review:** Reviewed by `gpt-5.4` before implementation. Review outcome: proceed with additional safeguards for quality-script compatibility, exact dependency graph preservation, and final validation.

## Problem statement

The existing `src/` solution reflects the legacy LeanKernel architecture. The rearchitecture requires a clean .NET 10 solution scaffold with domain-driven project boundaries, explicit dependencies, and test projects so later work can be implemented without carrying forward the old structure.

## Scope

This task will:

1. Delete the existing visible contents of `src/`.
2. Create a new `src/LeanKernel.sln` solution.
3. Create the required class library, API, and test projects.
4. Add centralized build settings via `src/Directory.Build.props`.
5. Apply the required project-reference graph.
6. Add the requested NuGet packages, falling back only when an exact version is unavailable.
7. Remove template placeholder files and add minimal placeholders so each project builds.
8. Replace the Gateway template entry point with a minimal health endpoint.
9. Recreate any quality-script-required test settings files that would otherwise be removed with the legacy `src/` tree.
10. Validate with restore, build, test, coverage, and Sonar quality commands where the environment permits.

## Out of scope

- Implementing domain logic beyond scaffold placeholders.
- Adding real API endpoints beyond the health endpoint.
- Introducing migrations, EF models, or feature behavior beyond compilation-ready placeholders.
- Renaming the requested project boundaries.

## Required projects

- `LeanKernel.Abstractions`
- `LeanKernel.Agents`
- `LeanKernel.Context`
- `LeanKernel.Knowledge`
- `LeanKernel.Tools`
- `LeanKernel.Persistence`
- `LeanKernel.Diagnostics`
- `LeanKernel.Gateway`
- `LeanKernel.Tests.Unit`
- `LeanKernel.Tests.Integration`

## Dependency graph

- `LeanKernel.Agents` -> `LeanKernel.Abstractions`, `LeanKernel.Context`, `LeanKernel.Tools`
- `LeanKernel.Context` -> `LeanKernel.Abstractions`, `LeanKernel.Knowledge`, `LeanKernel.Persistence`
- `LeanKernel.Knowledge` -> `LeanKernel.Abstractions`
- `LeanKernel.Tools` -> `LeanKernel.Abstractions`, `LeanKernel.Knowledge`
- `LeanKernel.Persistence` -> `LeanKernel.Abstractions`
- `LeanKernel.Diagnostics` -> `LeanKernel.Abstractions`
- `LeanKernel.Gateway` -> `LeanKernel.Abstractions`, `LeanKernel.Agents`, `LeanKernel.Diagnostics`, `LeanKernel.Persistence`
- `LeanKernel.Tests.Unit` -> `LeanKernel.Abstractions`, `LeanKernel.Agents`, `LeanKernel.Context`, `LeanKernel.Knowledge`, `LeanKernel.Tools`, `LeanKernel.Persistence`, `LeanKernel.Diagnostics`
- `LeanKernel.Tests.Integration` -> `LeanKernel.Gateway`

## Package matrix

### LeanKernel.Abstractions
- No external packages.

### LeanKernel.Agents
- `Microsoft.Agents.AI` `1.6.1`
- `Microsoft.Agents.AI.OpenAI` `1.6.1`
- `Microsoft.Agents.AI.Workflows` `1.6.1`
- `Microsoft.Extensions.AI` `10.5.0`
- `Microsoft.Extensions.AI.OpenAI` `10.5.0`
- `OpenAI` `2.10.0`
- `Microsoft.Extensions.Options` `10.0.7`
- `Microsoft.Extensions.Logging.Abstractions` `10.0.7`

### LeanKernel.Context
- `Microsoft.Extensions.Options` `10.0.7`
- `Microsoft.Extensions.Logging.Abstractions` `10.0.7`

### LeanKernel.Knowledge
- `Microsoft.Extensions.Http` `10.0.7`
- `Microsoft.Extensions.Options` `10.0.7`
- `Microsoft.Extensions.Logging.Abstractions` `10.0.7`

### LeanKernel.Persistence
- `Microsoft.EntityFrameworkCore` `10.0.1`
- `Npgsql.EntityFrameworkCore.PostgreSQL` preferred `10.0.4`; nearest compatible version allowed if unavailable
- `Microsoft.Extensions.Options` `10.0.7`
- `Microsoft.Extensions.Logging.Abstractions` `10.0.7`

### LeanKernel.Diagnostics
- `OpenTelemetry.Api` `1.15.3`
- `Serilog` preferred `5.0.1`; nearest compatible version allowed if unavailable
- `Microsoft.Extensions.Logging.Abstractions` `10.0.7`

### LeanKernel.Gateway
- `Serilog.Extensions.Hosting` `9.0.0`
- `Serilog.Settings.Configuration` `9.0.0`
- `Serilog.Sinks.Console` `6.1.1`

### LeanKernel.Tests.Unit
- `Moq` preferred `4.20.72`; nearest compatible version allowed if unavailable
- `FluentAssertions` preferred `8.3.0`; nearest compatible version allowed if unavailable

### LeanKernel.Tests.Integration
- `Microsoft.AspNetCore.Mvc.Testing` `10.0.7`

## Implementation notes

- Use `Directory.Build.props` for shared target framework and compiler defaults.
- Add `GlobalUsings.cs` placeholder files to each class library and test project after removing `Class1.cs`.
- Replace the Gateway template `Program.cs` with a minimal app exposing `GET /api/health`.
- Remove template artifacts such as `WeatherForecast.cs` and generated controllers if present.
- Recreate `coverage.runsettings` and `coverage.sonar.runsettings` in `LeanKernel.Tests.Unit` so repository quality scripts continue to work after deleting the legacy `src/` tree.
- If the local shell lacks a `dotnet` installation, use a .NET 10 SDK container mounted at the repository root for scaffolding and validation.

## Validation plan

1. `dotnet restore src/LeanKernel.sln`
2. `dotnet build src/LeanKernel.sln --no-restore -v minimal`
3. `dotnet test src/LeanKernel.sln --no-build -v minimal`
4. `scripts/quality/test-coverage.sh`
5. `scripts/quality/sonarqube-scan.sh`

## Acceptance criteria

- The legacy `src/` tree is replaced by the requested scaffold.
- All required projects exist and are added to `src/LeanKernel.sln`.
- Project references match the specified dependency graph with no circular dependencies.
- Requested packages are installed, with any version fallback documented by actual restore output.
- Placeholder/template files are removed or replaced with minimal valid code.
- `dotnet restore`, `dotnet build`, and `dotnet test` succeed.
- Coverage and Sonar commands succeed, or any environment blocker is captured with evidence.
- SQL todo `p1-scaffold` is marked `done` only after successful validation.
