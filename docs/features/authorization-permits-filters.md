# Authorization Permits And Filters

LeanKernel enforces user-data isolation through a permit/filter/repository pipeline.

## What Is Enforced

- `IPermit<TEntity>` controls operation-level authorization (`Create`, `Read`, `Update`, `Delete`).
- `IFilter<TEntity>` contributes scoped read predicates.
- `IRepository<TEntity>` applies `IFilter<TEntity>` on reads and `IPermit<TEntity>` checks on writes.

This pattern centralizes tenant/user/channel partitioning so feature code does not need to duplicate ad-hoc triad filters.

## Scope Policy Model

Scope policy definitions are resolved from `Agents:EntityScopePolicies` and mapped per entity type.

- Dimensions: `Tenant`, `User`, `Channel` (`ScopeDimension` flags)
- Optional navigation path support (for example `Session` or `Turn.Session`)
- Fail-closed behavior for unknown entity policy resolution

## Runtime Components

- `ScopeFilterBuilder`: builds EF-translatable predicates from permit identity + configured policy
- `ScopeDrivenFilter<TEntity>`: composes scope predicate with soft-delete enforcement
- `ConfigurationScopePolicyProvider`: resolves configured scope policy by CLR entity type
- `RequestContextPermit<TEntity>`: request-scoped operation checks using claims and current request identity
- `EntityRepository<TEntity>`: shared data access path for scoped entities

## Current Consumers

The following services now use repository-scoped access:

- `DbChatHistoryProvider`
- `TelemetryAggregationService`
- `TelemetryExportService`

## Related

- [Identity partitioning](identity-partitioning.md)
- [Architecture](../architecture/index.md)
