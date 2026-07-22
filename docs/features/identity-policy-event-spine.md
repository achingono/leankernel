# Identity, Policy, And Event Spine

Phase 20 establishes the architectural foundations for future phases: a canonical identity model, a shared in-process policy core, and an append-only event spine.

## Canonical Identity Model

`IdentityContext` (`src/Common/LeanKernel.Core/IdentityContext.cs`) is an immutable record that exposes all identity dimensions resolved for the current request. It preserves the existing split between person-scoped memory, user-scoped transcript/session ownership, and anonymous session isolation.

| Dimension | Source | Used By |
|---|---|---|
| `TenantId` | Request host -> `TenantResolutionMiddleware` | Data partitioning across all entities |
| `PersonId` | `UserEntity.PersonId` (defaults to `UserEntity.Id`) | Memory scope (`MemoryScope`) |
| `UserId` | Authenticated claims or guest-user fallback | Session/transcript ownership |
| `ChannelId` | OpenAI HTTP surface | Channel-scoped partitioning |
| `SessionId` | ASP.NET session (anonymous only) | Anonymous session isolation |

Constructed from `IPermit` via `IdentityContext.FromPermit()`.

## Policy Core

The policy core (`src/Common/LeanKernel.Logic/Policy/`) provides domain-level policy evaluation that composes with (rather than replaces) the existing Phase 19 permit/filter/repository enforcement path.

### Interfaces

| Interface | Purpose |
|---|---|
| `IPolicyContext` | Carries identity + permit + metadata for evaluation |
| `IPolicy<TEntity>` | Evaluates a domain rule against an entity and context |
| `IPolicyEvaluator` | Aggregates policies: short-circuit (first deny) or full enumeration |

### Default Policies

| Policy | Entity | Purpose |
|---|---|---|
| `IdentityLinkingPolicy` | `UserEntity` | Validates guest users are not linked to a different person |
| `MemoryAccessPolicy` | `ChannelMemoryPolicyEntity` | Validates channel is in memory share list |
| `AuthorizationGatePolicy<TEntity>` | Any | Wraps `IPermit<TEntity>.Can(Operation)` as a domain policy |
| `BudgetCheckPolicy` | Any | Ensures authenticated identity for budget-sensitive operations |

### Key Design Decisions

- Policies are evaluated above the repository layer and compose with `IPermit<TEntity>` / `IFilter<TEntity>` / `IRepository<TEntity>`.
- The repository remains the single fail-closed gate for data access.
- Policy evaluation stays in-process (no micro-service) until operational scale justifies a split.

## Event Spine

The event spine (`src/Common/LeanKernel.Core/Events/` + `src/Common/LeanKernel.Logic/Events/`) introduces append-only event contracts that coexist with the current `SessionEntity` / `TurnEntity` / `TurnTelemetryEntity` persistence model.

### Event Types

| Event | Envelope Type | Payload |
|---|---|---|
| `TurnEvent` | `EventEnvelope` | Role, content, author, session/idempotency keys |
| `ToolCallEvent` | `EventEnvelope` | Tool name, arguments, result, error, duration |
| `TelemetryEvent` | `EventEnvelope` | Model, tokens, cost, latency |

### Event Envelope (`EventEnvelope`)

Every event carries:
- `EventId` (unique), `EventType`, `SchemaVersion`
- Partitioning: `TenantId`, `PersonId`, `UserId`, `ChannelId`, optional `SessionId`
- `Timestamp`, `CorrelationId`, `CausationId`

### Infrastructure

| Component | Purpose |
|---|---|
| `IEventCollector` / `EventCollector` | Request-scoped event accumulation (concurrent queue) |
| `IEventStore` | Async persistence contract (default implementation: `DbEventStore`) |

### Migration / Coexistence Model

The current `TurnEntity` and `TurnTelemetryEntity` persistence is preserved as the primary write path. Event emission runs alongside it:

1. `DbChatHistoryProvider` emits `TurnEvent` + `TelemetryEvent` after each persisted turn
2. Events are collected in the request-scoped `EventCollector`
3. Collected events are durably appended through `DbEventStore` to `Events`
4. Future phases can derive read models directly from the event spine table

## First-Adopter Migration

`DbChatHistoryProvider` is the first consumer path wired to both the event spine and the policy core.

## Gateway Guardrails

- Policy evaluation, event spine, and data access logic live in `LeanKernel.Logic` (shared), not `LeanKernel.Gateway` (host).
- Gateway composition is limited to DI registration via `services.AddEventSpine()` and `services.AddPolicyCore()`.
- Gateway does not implement `IPolicy<TEntity>` directly.

## DI Registration

```csharp
// In Logic IServiceCollectionExtensions
services.AddEventSpine();   // IEventCollector, IEventStore
services.AddPolicyCore();   // IPolicyContext, IPolicyEvaluator, default policies

// In Gateway Program.cs
builder.Services.AddEventSpine();
builder.Services.AddPolicyCore();
```

## Extension Points

- Add new policies by implementing `IPolicy<TEntity>` and registering via DI
- Replace `IEventStore` with a concrete implementation (e.g., DbEventStore, Event Hub)
- Add new event types by defining a record + envelope + emit method on `IEventCollector`

## Non-Goals

- The policy core is not a micro-service (remains in-process)
- The event spine does not replace current turn/telemetry persistence (coexists)
- Cross-tenant identity linking is not supported
- Channel connectors, UI, and model-routing are unaffected

## Related

- [Identity partitioning](identity-partitioning.md)
- [Authorization permits and filters](authorization-permits-filters.md)
- [Model telemetry](model-telemetry.md)
- [Architecture](../architecture/index.md)
