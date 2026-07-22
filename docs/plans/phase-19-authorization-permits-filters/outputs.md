# Phase 19 Outputs

## Mandatory Outputs

| Output | Description | Format |
|---|---|---|
| `IEntity` entity headers | `: IEntity` interface implementation added to `SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `TenantEntity` | C# source |
| `IPermit<TEntity>` interface | Generic permit in `src/Common/LeanKernel.Core/Interfaces/IPermit.cs` under `namespace LeanKernel;` | C# source |

| `IFilter<TEntity>` interface | Filter interface in `LeanKernel.Logic/Interfaces/IFilter.cs` | C# source |
| `IRepository<TEntity>` interface | Repository contract in `LeanKernel.Logic/Interfaces/IRepository.cs` | C# source |
| `ScopeDimension` enum | Flags enum in `LeanKernel.Core/` | C# source |
| `EntityScopePolicy` + `EntityScopePolicies` | Config models under Logic configuration, bound from `Agents:EntityScopePolicies` | C# source |
| `IScopePolicyProvider` interface + `ConfigurationScopePolicyProvider` | Policy resolution in `LeanKernel.Logic/Filters/` | C# source |
| `ScopeFilterBuilder` | Expression builder with navigation safety in `LeanKernel.Logic/Filters/` | C# source |
| `ScopeDrivenFilter<TEntity>` | Open-generic filter in `LeanKernel.Logic/Filters/` | C# source |
| `RequestContextPermit<TEntity>` | Concrete permit in `LeanKernel.Gateway/Providers/` | C# source |
| `EntityRepository<TEntity>` | Guid-key repository (`where TEntity : class, IEntity`) in `LeanKernel.Logic/Repositories/` | C# source |
| `AddPermits()` DI extension | Gateway `IServiceCollectionExtensions` | C# source |
| `AddFilters()` DI extension | Logic `IServiceCollectionExtensions` | C# source |
| Migrated consumers (3 files) | `DbChatHistoryProvider` (`IRepository` for Session, Turn, Telemetry), `TelemetryAggregationService` (`IRepository<TurnTelemetryEntity>`), `TelemetryExportService` (`IRepository<TurnTelemetryEntity>`) | C# source |
| Unit + integration tests | Tests for all new types, expression caching, navigation safety, user data repository enforcement, and consumer parity | C# source |
| Updated docs | `solution-structure.md`, `identity-partitioning.md`, `appsettings-reference.md` | Markdown |

## Optional Outputs
- `DefaultPermit<TEntity>` for test/debug scenarios (always returns `false` for `Can(Operation)`).
- Follow-on phase note for string-key repositories (`AgentStateEntity`).

## Output Quality Checklist
- [x] All mandatory outputs produced
- [x] All outputs reviewed before gate
- [x] Evidence log updated with output references
