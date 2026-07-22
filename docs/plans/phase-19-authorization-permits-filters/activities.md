# Phase 19 Activities

## Analysis Summary

### Famorize Pattern To Reuse

| Interface | Location | Purpose |
| --- | --- | --- |
| `IPermit` / `IPermit<TEntity>` | `~/source/repos/famorize/src/Common/Famorize.Core/Interfaces/IPermit.cs` | Identity + operation gating (`Can(Operation)`) |
| `IFilter<TEntity>` | `~/source/repos/famorize/src/Common/Famorize.Logic/Interfaces/IFilter.cs` | Central query predicate |
| `EntityRepository<TEntity>` | `~/source/repos/famorize/src/Common/Famorize.Logic/Repositories/EntityRepository.cs` | Applies filter and permit consistently |

LeanKernel should copy this composition model, but with repository-specific constraints:
- Keep current identity flow (`TenantResolutionMiddleware` -> `RequestContextPermit`) unchanged.
- Scope constraints are mostly structural (`TenantId`, `UserId`, `ChannelId`), not deep business rules.

### Entity Scope Map (LeanKernel)

| Entity | Scope source | Recommended default policy |
| --- | --- | --- |
| `SessionEntity` | Direct keys | `Tenant|User|Channel` |
| `TurnEntity` | `Session` navigation | `Tenant|User|Channel`, `NavigationPath=Session` |
| `TurnTelemetryEntity` | `Turn.Session` navigation | `Tenant|User|Channel`, `NavigationPath=Turn.Session` |
| `ChannelMemoryPolicyEntity` | Direct keys | `Tenant|Channel` |
| `ChannelSenderBindingEntity` | Direct keys | `Tenant|User|Channel` |
| `AgentStateEntity` | Direct keys, string PK | Keep out of first migration |

### Known Constraints To Honor

1. Config shape must stay under existing top-level sections. New policy config goes under `Agents:EntityScopePolicies`.
2. Unknown policy resolution must fail closed.
3. All domain code reading/writing user data (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, etc.) MUST use `IRepository<TEntity>` so permits and filters are enforced without bypasses.
4. Anonymous requests already resolve to a persisted guest `UserId`; no `SessionId`-field fallback in this phase.
5. Guid-key repository support only in this phase (`IEntity` entities). `AgentStateEntity` remains direct-store.
6. Existing channel memory sharing behavior must remain intact (`IChannelMemoryPolicyResolver`, `MemoryProvider`, `GBrainMemoryClient`).

---

## Step-By-Step Activities

### Activity 1: Add `IEntity` Prerequisite & `IPermit<TEntity>`

**Files**
- `src/Common/LeanKernel.Core/Entities/SessionEntity.cs`
- `src/Common/LeanKernel.Core/Entities/TurnEntity.cs`
- `src/Common/LeanKernel.Core/Entities/TurnTelemetryEntity.cs`
- `src/Common/LeanKernel.Core/Entities/UserEntity.cs`
- `src/Common/LeanKernel.Core/Entities/TenantEntity.cs`
- `src/Common/LeanKernel.Core/Interfaces/IPermit.cs`

**Work**

1. **Activity 1a (Prerequisite)**: Add `: IEntity` interface implementation to class headers for all Phase 19 in-scope entities (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `TenantEntity`). All 5 entities already possess `public Guid Id { get; set; }`, so no DB schema or EF migration is required. This guarantees 100% of in-scope entities satisfy `where TEntity : class, IEntity` and can be managed via `IRepository<TEntity>` without exceptions.

2. **Activity 1b**: Add generic interface in `namespace LeanKernel;` (matching non-generic `IPermit`):

```csharp
namespace LeanKernel;

public interface IPermit<TEntity> : IPermit where TEntity : class
{
    bool Can(Operation operation);
}
```

3. Keep claim contract canonical across all docs/code:
- claim type: `right`
- claim value: `{Operation}:{EntityName}`


### Activity 2: Add Scope Policy Models

**Files**
- `src/Common/LeanKernel.Core/ScopeDimension.cs` (new)
- `src/Common/LeanKernel.Logic/Configuration/EntityScopePolicies.cs` (new)

**Work**
1. Add flags enum:

```csharp
[Flags]
public enum ScopeDimension
{
    None = 0,
    Tenant = 1,
    User = 2,
    Channel = 4,
}
```

2. Add config models:
- `EntityScopePolicies` (collection root)
- `EntityScopePolicy` (`EntityType`, `Dimensions`, optional `NavigationPath`, optional `RequireAuthentication`)

3. (Model definition only — actual DI binding with `services.Configure<EntityScopePolicies>(...)` happens in Activity 8.)

**Namespace**: `LeanKernel.Logic.Filters`.

### Activity 3: Add Policy Provider (Fail Closed)

**Files**
- `src/Common/LeanKernel.Logic/Filters/IScopePolicyProvider.cs` (new)
- `src/Common/LeanKernel.Logic/Filters/ConfigurationScopePolicyProvider.cs` (new)

**Work**
1. Resolve policy by CLR type.
2. If policy missing for a scoped entity type, fail closed by throwing at startup validation.
3. Keep defaults for known entities via `PostConfigure`, but do not silently allow unknown scoped entities.

### Activity 4: Add `ScopeFilterBuilder`

**Files**
- `src/Common/LeanKernel.Logic/Filters/ScopeFilterBuilder.cs` (new)

**Namespace**: `LeanKernel.Logic.Filters`.

**Work**
1. Build EF-translatable predicates from policy + permit. Each dimension in the policy produces an equality check: `e.TenantId == permit.TenantId`, `e.UserId == permit.UserId`, etc. Multiple dimensions are combined with `&&`.
2. Support navigation paths (`Session`, `Turn.Session`). When set, the equality check is resolved through the navigation: `e.Session.TenantId` instead of `e.TenantId`. Ensure expression tree generation uses safe property navigation without invalid EF syntax.
3. Cache built predicate expressions per `(Type EntityType, ScopeDimension Dimensions, string? NavigationPath)` to minimize expression tree construction overhead during query evaluation.
4. Compose dimensions with logical `AND`.
5. No `SessionId` fallback logic in this phase.

### Activity 5: Add `IFilter<TEntity>` + Generic Filter

**Notes**: `src/Common/LeanKernel.Logic/Interfaces/` is a new directory.

**Files**
- `src/Common/LeanKernel.Logic/Interfaces/IFilter.cs` (new) — namespace `LeanKernel.Logic.Interfaces`
- `src/Common/LeanKernel.Logic/Filters/ScopeDrivenFilter.cs` (new) — namespace `LeanKernel.Logic.Filters`

**Work**
1. `IFilter<TEntity>` keeps single `Predicate` property.
2. `ScopeDrivenFilter<TEntity>` logic:
- if `Can(Read)` is true -> return soft-delete-only predicate (or null where not applicable)
- else -> apply policy predicate + soft-delete predicate

### Activity 6: Add `RequestContextPermit<TEntity>`

**Files**
- `src/Services/LeanKernel.Gateway/Providers/RequestContextPermitOfT.cs` (new)

**Work**
1. Inject existing scoped `IPermit` and `IPrincipalAccessor`.
2. Delegate all base properties to injected `IPermit` (no manual `new RequestContextPermit(...)`).
3. Implement `Can(Operation)` with canonical claim format and admin bypass.

**Namespace**: `LeanKernel.Gateway.Providers`.

### Activity 7: Repository Design (Guid-Key Only) & User Data Protection

**Notes**: `src/Common/LeanKernel.Logic/Repositories/` is a new directory. `src/Common/LeanKernel.Logic/Interfaces/` from Activity 5 is the same new directory.

**Files**
- `src/Common/LeanKernel.Logic/Interfaces/IRepository.cs` (new)
- `src/Common/LeanKernel.Logic/Repositories/EntityRepository.cs` (new)

**Work**
1. Limit first phase to Guid-key entities by constraining generic type:

```csharp
public interface IRepository<TEntity> where TEntity : class, IEntity
```

2. Inject scoped `EntityContext` (not context factory) so returned `IQueryable` instances remain safe within request scope.
3. Apply `IFilter<TEntity>.Predicate` in read paths (`GetAll()`, `GetByIdAsync()`, `FindAsync()`).
4. Enforce `Can(Create/Update/Delete)` via `IPermit<TEntity>` in all write paths (`AddAsync()`, `UpdateAsync()`, `DeleteAsync()`).
5. Stamp auditable fields only for `IAuditable` entities.
6. Stamp partition keys by convention only when matching properties exist.
7. Architectural Rule: All domain logic handling user entities (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `ChannelSenderBindingEntity`, `ChannelMemoryPolicyEntity`) MUST consume `IRepository<TEntity>` so no user data query bypasses permit checks or filters.

### Activity 8: DI Wiring

**Files**
- `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs`
- `src/Services/LeanKernel.Gateway/Extensions/IServiceCollectionExtensions.cs`
- `src/Services/LeanKernel.Gateway/Programs.cs`

**Work**
1. Keep existing `services.AddScoped<IPermit, RequestContextPermit>();` registration.
2. Add `services.AddScoped(typeof(IPermit<>), typeof(RequestContextPermit<>));`.
3. Register open generic filter: `services.AddScoped(typeof(IFilter<>), typeof(ScopeDrivenFilter<>));`.
4. Register `ScopeFilterBuilder` and `IScopePolicyProvider`.
5. Register open generic repository: `services.AddScoped(typeof(IRepository<>), typeof(EntityRepository<>));`.
6. Bind `Agents:EntityScopePolicies` with startup validation.

### Activity 9: Consumer Migrations to `IRepository<TEntity>`

**Files**
1. `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`
2. `src/Common/LeanKernel.Logic/Telemetry/TelemetryAggregationService.cs`
3. `src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs`

**Not in this phase**
- `src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs` (string PK)
- `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` (uses `GBrainMemoryClient` transport)
- `src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs` (vector store integration)

**Migration Implementation Details**
- `DbChatHistoryProvider`: Replace direct `IDbContextFactory<EntityContext>` usage with constructor-injected `IRepository<SessionEntity>`, `IRepository<TurnEntity>`, and `IRepository<TurnTelemetryEntity>`.
  - Verification of session ownership becomes a repository read (`sessionRepo.GetByIdAsync(...)` or `sessionRepo.GetAll().AnyAsync(...)`).
  - Turn retrieval uses `turnRepo.GetAll().Where(t => t.SessionId == sessionGuid)`.
  - Session creation / turn persistence uses `sessionRepo.AddAsync(...)`, `turnRepo.AddAsync(...)`, and `telemetryRepo.AddAsync(...)`.
- `TelemetryAggregationService`: Replace `IDbContextFactory<EntityContext>` with `IRepository<TurnTelemetryEntity>`. Queries consume `telemetryRepo.GetAll()`, which automatically applies the `Turn.Session` navigation filter predicate.
- `TelemetryExportService`: Replace `IDbContextFactory<EntityContext>` with `IRepository<TurnTelemetryEntity>`. Exports consume `telemetryRepo.GetAll()`, automatically filtered by tenant/user/channel permit and scope policy.

**Guardrails**
- Ensure no raw `EntityContext` queries remain in these domain services.
- Do not bypass or replace `IChannelMemoryPolicyResolver` on the memory path. Scope filtering changes in this phase must not alter cross-channel memory sharing resolution.

### Activity 10: Test + Quality Gates

**Tests**
1. Permit tests for auth/admin/claim behavior.
2. Scope builder tests for direct and navigation scope expressions, including expression caching and navigation nullability checks.
3. Fail-closed tests for missing policies.
4. Repository tests for filtered reads, operation checks, audit/partition stamping, and write permission gating.
5. Equivalence tests for the 3 migrated consumers verifying parity between old ad-hoc queries and new repository-backed execution.
6. Regression tests ensuring no user data queries bypass `IRepository<TEntity>`.
7. Regression tests for channel memory sharing:
- read-path fan-out respects `ReadableChannelIds`
- write-path related-memory lookup respects `MutuallyVisibleChannelIds`
- directional policy behavior (Share AND Access) remains unchanged

**Gates**
1. Coverage >= 80% for changed scope.
2. `scripts/quality/sonarqube-scan.sh` with no new Blocker/Critical/Major issues.
3. Deep review sub-agent run and findings addressed.

---

## Review Focus

- [ ] Policy configuration is under `Agents:EntityScopePolicies` only.
- [ ] Unknown entity scope policy fails closed.
- [ ] All domain code accessing user data uses `IRepository<TEntity>` so permits and filters are enforced without bypasses.
- [ ] Claim contract uses only `type=right` + `value={Operation}:{EntityName}`.
- [ ] Repository does not assume non-Guid keys.
- [ ] `IQueryable` lifetime is safe (scoped `EntityContext`).
- [ ] `ScopeFilterBuilder` caches expression trees and handles navigation path nullability cleanly.
- [ ] Anonymous behavior matches current `UserId`-based guest identity.
- [ ] Migration scope covers all designated user data consumers (`DbChatHistoryProvider`, telemetry aggregation/export).
- [ ] Channel memory sharing policy enforcement remains unchanged on memory read/write paths.

