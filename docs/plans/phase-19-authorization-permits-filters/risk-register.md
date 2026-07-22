# Phase 19 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Navigation predicates (`Turn.Session`, `Turn.Session.*`) fail translation on target provider | Runtime query failures | Add SQLite and target-provider integration tests for each navigation policy path | Open |
| R2 | Unknown entity policy accidentally allows unscoped reads | Cross-tenant/user/channel leakage | Enforce fail-closed startup validation for scoped entities missing policies | Open |
| R3 | Misconfigured policy omits `Tenant` for scoped entity | Cross-tenant leakage | Validate policies at startup; reject scoped policies that omit `Tenant` unless explicitly documented exception | Open |
| R4 | Repository assumes Guid keys for all entities | Runtime errors for string/non-Guid keys | Constrain repository to `IEntity` and defer non-Guid entities (`AgentStateEntity`) to follow-on phase | Open |
| R5 | Returning `IQueryable` from factory-created context causes disposed-context bugs | Production failures under load | Use scoped `EntityContext` in repository; avoid context factory for query-returning methods | Open |
| R6 | Claim contract inconsistency causes `Can(Operation)` mismatches | Incorrect authorization outcomes | Standardize claim model: `type=right`, `value={Operation}:{EntityName}` | Open |
| R7 | Migration modifies behavior compared to existing ad-hoc filters | Regression in chat history/telemetry | Add old-vs-new equivalence tests for the three migrated consumers | Open |
| R8 | Scope policy config placed outside allowed configuration shape | Architectural drift from AGENTS guidance | Bind configuration under `Agents:EntityScopePolicies` and document in appsettings reference | Open |
| R9 | Domain feature handling user data bypasses `IRepository<TEntity>` by querying `EntityContext` directly | Scope filters and permit checks bypassed for user data | Enforce strict rule: all domain services handling user entities must consume `IRepository<TEntity>`; verify in deep review | Open |
| R10 | Expression tree building overhead or navigation property nullability errors | Performance degradation or runtime expression translation failure | Cache compiled expressions per entity/dimension/path in `ScopeFilterBuilder` and test navigation path expression building | Open |

## Open Decisions

1. **Generic-only filter vs per-entity overrides**
   - Decision: Use open-generic `ScopeDrivenFilter<TEntity>` as default. Add per-entity override only when business rules exceed structural scope dimensions.

2. **Unknown policy behavior**
   - Decision: Fail closed. Startup validation fails for scoped entities missing policies/defaults.

3. **Repository key strategy**
   - Decision: Phase 19 supports Guid-key entities only (`IEntity`). Non-Guid repositories are a separate follow-on change.

4. **Anonymous scope behavior**
   - Decision: Keep current guest `UserId` partition model. No `SessionId` property fallback in this phase.

5. **User Data Access Policy**
   - Decision: Strict enforcement. All domain code handling user entities (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `ChannelSenderBindingEntity`, `ChannelMemoryPolicyEntity`) MUST access database entities through `IRepository<TEntity>`.

