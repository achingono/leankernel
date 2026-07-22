# Phase 19 - Authorization Permits And Filters

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective

Introduce `IPermit<TEntity>` and `IFilter<TEntity>` interfaces (inspired by famorize) into LeanKernel to centralize authorization checks and data partitioning predicates. Today multiple services duplicate manual `TenantId/UserId/ChannelId` filtering, and scoping rules are scattered across call sites.

This phase introduces a configurable scope policy model that supports tenant-level, user-level, and channel-level constraints per entity type. Policies are resolved from options and enforced through a centralized filter pipeline and generic repository pattern so data partitioning is consistent, testable, and auditable across all user data features.

Key constraints adopted by this plan revision:
- Fail closed for unknown entity scope policies (no implicit unscoped reads).
- Strict user data enforcement: All domain feature code handling user data (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `ChannelSenderBindingEntity`, `ChannelMemoryPolicyEntity`) MUST access database entities via `IRepository<TEntity>` so permits and filters cannot be bypassed.
- Keep configuration shape under existing sections (`Agents`), not new top-level sections.
- Prerequisite: Standardize `IEntity` implementation on all Phase 19 in-scope entities (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, `TenantEntity`), which already possess `public Guid Id { get; set; }`. This requires zero DB migrations and ensures 100% of in-scope entities support `IRepository<TEntity>` without exception.
- Standardize `IPermit<TEntity>` under `namespace LeanKernel;` alongside non-generic `IPermit`.
- Keep anonymous behavior aligned with current middleware identity resolution (anonymous already maps to a persisted guest `UserId`; no `SessionId` fallback in this phase).
- Restrict first-phase repository adoption to Guid-key entities (`IEntity`); keep `DbAgentStateStore` unchanged for now (string-keyed primary key).

## Scope

### In Scope
- Prerequisite: Standardize `IEntity` implementation on `SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`, `UserEntity`, and `TenantEntity` (non-breaking, 0 migration).
- Add `IPermit<TEntity> : IPermit` to `LeanKernel.Core` under `namespace LeanKernel;` with `Can(Operation operation)` for claims-based permission gating.
- Add `IFilter<TEntity>` to `LeanKernel.Logic` under `LeanKernel.Logic.Interfaces` with `Expression<Func<TEntity, bool>>? Predicate`.
- Define `ScopeDimension` and `EntityScopePolicy` to model per-entity scope dimensions and optional navigation path.
- Implement a `ScopePolicyProvider` using `IOptions<...>` bound from `Agents:EntityScopePolicies` with safe defaults and startup fail-closed validation.
- Implement `ScopeFilterBuilder` to build EF-translatable predicates for direct and navigation-based scope properties with nullability safety and expression tree caching.
- Implement a single open-generic `ScopeDrivenFilter<TEntity>` that applies policy-derived scope plus soft-delete (`IRecyclable`) constraints.
- Implement `RequestContextPermit<TEntity>` in the Gateway delegating to request-scoped `IPermit`.
- Implement Guid-key repository abstraction (`IRepository<TEntity>`) for entities implementing `IEntity`; apply filters on reads and enforce operation permits on writes.

- Register all types via `AddPermits()` and `AddFilters()` DI extensions.
- Migrate consumers handling user data (`DbChatHistoryProvider`, `TelemetryAggregationService`, `TelemetryExportService`) to use `IRepository<TEntity>` injections (`SessionEntity`, `TurnEntity`, `TurnTelemetryEntity`) so no user data query bypasses permits or filters.
- Update unit and integration tests for all new types and migrated consumers.

### Out of Scope
- Replacing `IdentityResolver` or `TenantResolutionMiddleware` (they remain the identity source of truth for bootstrapping request identity before `IPermit` is available).
- Adding new auth providers or auth middleware.
- Migrating `DbAgentStateStore` to the generic repository in this phase (string-keyed primary key).
- Dynamic scope policies that change mid-request (policies are resolved per-request but stable during request execution).
- A UI to manage scope policies.

## Status
**Closed** — implementation, verification, and documentation evidence are complete for Phase 19.

## Current Implementation Status

- ✅ Permit/filter/repository architecture implemented and wired in DI.
- ✅ User-data consumers migrated: `DbChatHistoryProvider`, `TelemetryAggregationService`, `TelemetryExportService`.
- ✅ Deep-review findings addressed (guest policy handling, default scope-policy `PostConfigure`, audit badge stamping, dead cache removal).
- ✅ Verification completed: unit tests passing, SonarQube quality gate passing.
- ✅ Exit checklist evidence captured and plan closed.

## Entry Criteria
- [x] `IPermit` (non-generic) exists with `PersonId`, `UserId`, `TenantId`, `ChannelId`, `HostName`, `IsAuthenticated`, `SessionId`, `Badge`.
- [x] `RequestContextPermit` reads from `HttpContext.Items`.
- [x] `Operation` enum exists with `Create`, `Read`, `Update`, `Delete`.
- [x] Entity ownership map is documented (see `activities.md`).

## Exit Criteria
`IPermit<TEntity>` and `IFilter<TEntity>` are defined, implemented, and consumed by repository/query layers for Guid-key entities. All domain code reading/writing user data consumes `IRepository<TEntity>`, guaranteeing permits and filters are strictly enforced without ad-hoc bypasses. Scope policies are configurable via options under `Agents:EntityScopePolicies`, enforced fail-closed, and correctly constrain queries to tenant/user/channel level per entity. At least 3 existing ad-hoc query sites use the new pattern. All existing tests pass, coverage is at least 80%, Sonar major+ issues are addressed, and deep-review findings are resolved. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
