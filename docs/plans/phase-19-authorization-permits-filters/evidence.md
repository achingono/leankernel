# Phase 19 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Famorize IPermit (non-generic) | `~/source/repos/famorize/src/Common/Famorize.Core/Interfaces/IPermit.cs` | `Id`, `Badge`, `HostName` identity context |
| Famorize IPermit<TEntity> | Same file | `Can(Operation)` CRUD gating by entity type |
| Famorize IFilter<TEntity> | `~/source/repos/famorize/src/Common/Famorize.Logic/Interfaces/IFilter.cs` | Single `Predicate` property pattern |
| Famorize ClaimsPermit<TEntity> | `~/source/repos/famorize/src/Services/Famorize.Api/Authorization/Permits/ClaimsPermit.cs` | Claim-based `Can(Operation)` with admin bypass |
| Famorize EntityRepository<TEntity> | `~/source/repos/famorize/src/Common/Famorize.Logic/Repositories/EntityRepository.cs` | Repository composing IPermit + IFilter |
| Famorize DI: AddPermits | `~/source/repos/famorize/src/Services/Famorize.Api/Extensions/IServiceCollectionExtensions.cs` (line 500) | Open-generic `IPermit<TEntity>` → `ClaimsPermit<TEntity>` |
| Famorize DI: AddFilters | `~/source/repos/famorize/src/Common/Famorize.Logic/Extensions/IServiceCollectionExtensions.cs` (line 33) | 21 per-entity scoped filter registrations |
| Existing LeanKernel IPermit | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs` | 8 properties: PersonId, UserId, TenantId, ChannelId, HostName, IsAuthenticated, SessionId, Badge |
| Existing LeanKernel RequestContextPermit | `src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs` | Reads from HttpContext.Items |
| Existing LeanKernel Operation enum | `src/Common/LeanKernel.Core/Operation.cs` | Create/Read/Update/Delete — already aligned |
| Existing ad-hoc triad pattern 1 | `src/Common/LeanKernel.Logic/Telemetry/TelemetryAggregationService.cs:253-257` | `.Where(row => row.Turn.Session.TenantId == ...)` — repetitive manual triad |
| Existing ad-hoc triad pattern 2 | `src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs:45-47` | Same nav-through pattern as aggregation |
| Existing ad-hoc triad pattern 3 | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs:51-58` | Direct triad on SessionEntity |
| Existing EntityContext | `src/Common/LeanKernel.Data/EntityContext.cs` | All 9 DbSets with global query filters for soft-delete only |
| Existing IRecyclable | `src/Common/LeanKernel.Core/Interfaces/IRecyclable.cs` | `IsDeleted` property for soft-delete predicates |
| Existing IAuditable | `src/Common/LeanKernel.Core/Interfaces/IAuditable.cs` | `CreatedOn/CreatedBy/UpdatedOn/UpdatedBy` for audit stamping |
| Existing IEntity | `src/Common/LeanKernel.Core/Interfaces/IEntity.cs` | `Guid Id` — entity identifier |
| Configuration options pattern | `src/Services/LeanKernel.Gateway/Programs.cs` | Existing options binding pattern; Phase 19 binds under `Agents:EntityScopePolicies` |
| IOptions pattern for code defaults | `src/Common/LeanKernel.Logic/Providers/ChannelMemoryPolicyResolver.cs:42-44` | Reads `agentSettings.Value.Channels.MemoryPolicyDefaults` for runtime defaults; Phase 19 can use similar `IOptions<T>.Value` pattern plus `PostConfigure` for fallback |

## Implementation Artifacts

### Interfaces (`src/Common/LeanKernel.Core/Interfaces/`)

| File | Lines | Purpose |
| --- | --- | --- |
| `IPermit.cs` | Existing (updated) | Added `IPermit<TEntity>` generic interface under `namespace LeanKernel;` |
| `IEntity.cs` | Existing | `public Guid Id { get; set; }` — used as repository constraint |

### Interfaces (`src/Common/LeanKernel.Logic/Interfaces/`)

| File | Lines | Purpose |
| --- | --- | --- |
| `IFilter.cs` | ~10 | `Expression<Func<TEntity, bool>>? Predicate { get; }` |
| `IRepository.cs` | ~30 | `IQueryable<TEntity> GetAll()`, `GetByIdAsync`, `Add`, `Update`, `Delete`, `SaveChangesAsync` |

### Configuration Models (`src/Common/LeanKernel.Logic/Configuration/`)

| File | Lines | Purpose |
| --- | --- | --- |
| `EntityScopePolicies.cs` | ~40 | `EntityScopePolicy` + `EntityScopePolicies` collection root |
| `AppConfiguration.cs` | Updated | Added `EntityScopePolicies` under `Agents` section |

### Scope & Filter Engine (`src/Common/LeanKernel.Logic/Filters/`)

| File | Lines | Purpose |
| --- | --- | --- |
| `ScopeFilterBuilder.cs` | ~120 | Expression building with navigation resolution + caching |
| `ScopeDrivenFilter.cs` | ~108 | Open-generic `IFilter<TEntity>` applying scope + soft-delete predicates |
| `IScopePolicyProvider.cs` | ~10 | Interface for policy resolution |
| `ConfigurationScopePolicyProvider.cs` | ~45 | Fail-closed policy provider with `PostConfigure` defaults |

### Core Types (`src/Common/LeanKernel.Core/`)

| File | Lines | Purpose |
| --- | --- | --- |
| `ScopeDimension.cs` | ~15 | `[Flags] enum ScopeDimension { None, Tenant, User, Channel }` |

### Repository (`src/Common/LeanKernel.Logic/Repositories/`)

| File | Lines | Purpose |
| --- | --- | --- |
| `EntityRepository.cs` | ~143 | Generic Guid-key repository: filter on reads, permit on writes, audit/partition stamping |

### Gateway Permit (`src/Services/LeanKernel.Gateway/Providers/`)

| File | Lines | Purpose |
| --- | --- | --- |
| `RequestContextPermitOfT.cs` | ~50 | `RequestContextPermit<TEntity>` delegating to scoped `IPermit` + claim-based `Can(Operation)` |

### DI Extensions

| File | Location | Purpose |
| --- | --- | --- |
| `IServiceCollectionExtensions.cs` (Logic) | `src/Common/LeanKernel.Logic/Extensions/` | `AddFilters()`: open-generic filter, policy provider, scope builder |
| `IServiceCollectionExtensions.cs` (Gateway) | `src/Services/LeanKernel.Gateway/Extensions/` | `AddPermits()`: open-generic permit; `AddRepositories()`: open-generic repo + config binding |
| `Programs.cs` | `src/Services/LeanKernel.Gateway/` | Calls `services.AddPermits().AddFilters().AddRepositories()` |

### Consumer Migrations

| Service | File | Change |
| --- | --- | --- |
| `DbChatHistoryProvider` | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs` | Replaced `IDbContextFactory<EntityContext>` with 3 `IRepository<T>` injections |
| `TelemetryAggregationService` | `src/Common/LeanKernel.Logic/Telemetry/TelemetryAggregationService.cs` | Replaced `IDbContextFactory<EntityContext>` with `IRepository<TurnTelemetryEntity>` |
| `TelemetryExportService` | `src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs` | Replaced `IDbContextFactory<EntityContext>` with `IRepository<TurnTelemetryEntity>` |

### Tests

| Test File | Tests | Purpose |
| --- | --- | --- |
| `test/.../Filters/ScopeFilterBuilderTests.cs` | 4 | Direct + navigation scope expression generation |
| `test/.../Repositories/EntityRepositoryTests.cs` | 6 | Filtered reads, write permission gating, audit stamping |
| `test/.../Providers/DbChatHistoryProviderTests.cs` | 14 | Integration: session ownership, turn window, tool roles, telemetry, scoping |
| `test/.../Telemetry/TelemetryAggregationServiceTests.cs` | 8 | Rollup queries, scoping, fallbacks, efficiency metrics |
| `test/.../Telemetry/TelemetryExportServiceTests.cs` | 5 | Export ordering, PII-free shape, fallback values, arg validation |

**Total tests: 682 unit passing; 28 integration passing; 5 Playwright passing**

## Verification Runs

| Check | Command | Result |
| --- | --- | --- |
| Formatting | `dotnet format` | Completed (no blocking formatting issues) |
| Unit tests | `dotnet test test/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj` | Passed (682/682) |
| Integration tests | `dotnet test test/LeanKernel.Tests.Integration/LeanKernel.Tests.Integration.csproj` | Passed (28/28) |
| Playwright tests | `dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj` | Passed (5/5) |
| Coverage gate | `scripts/quality/test-coverage.sh` | Passed (Line coverage 80.12%, threshold 80.00%) |
| SonarQube | `scripts/quality/sonarqube-scan.sh` | Quality Gate: **PASSED** |
| Deep review | `.agents/prompts/deep-review.prompt.md` (sub-agent review) | Findings addressed in scope/filter/repository implementation |

## Remaining Checklist Evidence

| Exit Criterion | Evidence |
| --- | --- |
| Equivalence tests confirm old/new parity | `test/LeanKernel.Tests.Unit/Providers/DbChatHistoryProviderTests.cs`, `test/LeanKernel.Tests.Unit/Telemetry/TelemetryAggregationServiceTests.cs`, `test/LeanKernel.Tests.Unit/Telemetry/TelemetryExportServiceTests.cs` |
| Channel memory sharing enforced by policy resolver | `test/LeanKernel.Tests.Unit/Identity/ChannelMemoryPolicyResolverTests.cs` |
| Memory read fan-out uses `ReadableChannelIds` | `test/LeanKernel.Tests.Unit/Providers/GBrainMemoryClientTests.cs` |
| Memory related-search uses `MutuallyVisibleChannelIds` | `test/LeanKernel.Tests.Unit/Providers/GBrainMemoryClientTests.cs`, `test/LeanKernel.Tests.Unit/Providers/MemoryProviderBehaviorTests.cs` |
