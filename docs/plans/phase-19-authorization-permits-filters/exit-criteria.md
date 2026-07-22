# Phase 19 Exit Criteria

## Gate Checklist

### Interfaces And Contracts
- [x] Prerequisite: `SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `TenantEntity` explicitly declare `: IEntity` interface implementation.
- [x] `IPermit<TEntity> : IPermit` exists under `namespace LeanKernel;` with `Can(Operation)`.
- [x] `IFilter<TEntity>` exists with `Expression<Func<TEntity, bool>>? Predicate`.
- [x] `IRepository<TEntity>` exists and is constrained to `where TEntity : class, IEntity`.
- [x] `ScopeDimension` and `EntityScopePolicy` models exist.


### Scope Policy Infrastructure
- [x] Policies are bound from `Agents:EntityScopePolicies` (no new top-level section).
- [x] Known entity defaults are added via `PostConfigure`.
- [x] Missing policy for a scoped entity fails closed at startup.
- [x] `ScopeFilterBuilder` generates EF-translatable predicates for:
  - [x] direct key scopes (`TenantId`, `UserId`, `ChannelId`)
  - [x] navigation scopes (`Session`, `Turn.Session`)
- [x] `ScopeFilterBuilder` safely handles property navigation without invalid EF syntax.

### Permit Implementation
- [x] `RequestContextPermit<TEntity>` delegates to injected scoped `IPermit`.
- [x] `Can(Operation)` uses canonical claim contract: claim type `right`, value `{Operation}:{EntityName}`.
- [x] Admin bypass works; unauthenticated access is policy-gated via `EntityScopePolicy.RequireAuthentication`.

### Repository Behavior & User Data Protection
- [x] Repository uses scoped `EntityContext` for safe `IQueryable` lifetime.
- [x] `GetAll()` applies filter predicate.
- [x] `GetByIdAsync()` returns null for out-of-scope records.
- [x] Write operations enforce `Can(Create/Update/Delete)` via `IPermit<TEntity>`.
- [x] Audit stamping applies only to `IAuditable` entities.
- [x] Partition key stamping applies only when matching properties exist.
- [x] Strict Rule: All domain logic code handling user data (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `ChannelSenderBindingEntity`, `ChannelMemoryPolicyEntity`) accesses entities strictly via `IRepository<TEntity>`.

### Migration Scope
- [x] `DbChatHistoryProvider` migrated to use `IRepository<SessionEntity>`, `IRepository<TurnEntity>`, and `IRepository<TurnTelemetryEntity>`.
- [x] `TelemetryAggregationService` migrated to use `IRepository<TurnTelemetryEntity>`.
- [x] `TelemetryExportService` migrated to use `IRepository<TurnTelemetryEntity>`.
- [x] `DbAgentStateStore` explicitly deferred from this phase (string-keyed primary key).
- [x] Equivalence tests confirm old/new behavior parity for migrated consumers.

### Context Semantics
- [x] Authenticated requests filter by configured scope dimensions.
- [x] Anonymous requests remain constrained by persisted guest `UserId` model.
- [x] No `SessionId` field fallback logic introduced in this phase.
- [x] Channel memory sharing remains enforced by `IChannelMemoryPolicyResolver`.
- [x] Memory read fan-out still uses effective `ReadableChannelIds`.
- [x] Memory normalization related-search still uses `MutuallyVisibleChannelIds`.

### Quality Gates
- [x] All existing tests pass.
- [x] Coverage for changed scope is >= 80%.
- [x] `scripts/quality/sonarqube-scan.sh` reports no new Blocker/Critical/Major issues.
- [x] Deep review sub-agent completed and findings addressed.
- [x] Documentation updated (`docs/features/identity-partitioning.md`, `docs/configuration/appsettings-reference.md`, `docs/architecture/solution-structure.md`).

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | Rebuild maintainer | Completed | Exit checklist and evidence reviewed. |
| Reviewer | Deep-review sub-agent | Completed | Findings documented and remediations verified. |
| Approver | Repository owner | Ready for sign-off | Technical closure complete; awaiting formal repo-level approval. |
