# Phase 20 Activities

## Step-By-Step Activities

1. Define the canonical identity model and invariants: tenant, person, user, channel, anonymous-session, and trust boundaries, including which runtime surfaces use each partition key.
2. Design and implement a shared policy core library with `IPolicyContext`, `IPolicy<TEntity>`, and `IPolicyEvaluator`.
3. Model policies for identity, authorization, memory, and budget decisions as reusable in-process rules, with explicit composition into the existing `IPermit<TEntity>` / `IFilter<TEntity>` / `IRepository<TEntity>` enforcement path rather than a parallel authorization plane.
4. Define an append-only event spine for turns, tool calls, and telemetry with an explicit event envelope (`TenantId`, `PersonId`, `UserId`, `ChannelId`, optional `SessionId`, timestamps, sequence/correlation identifiers, schema version) and derived read models instead of mutable history.
5. Define migration and coexistence rules between the event spine and the current `SessionEntity` / `TurnEntity` / `TurnTelemetryEntity` persistence model, including idempotency, soft-delete, compaction, and tool-call representation.
6. Migrate one first-adopter path to the new policy core and event contracts to prove the shape is sufficient without breaking the current repository/permit model.
7. Add gateway guardrails so host/composition concerns stay at the edge and business policy stays in shared code.
8. Add contract, policy, and event-spine tests plus documentation for adoption rules, extension points, and non-goals.

## Review Focus
- Canonical identity boundaries are explicit and stable.
- Canonical identity preserves the current split between person-scoped memory, user-scoped history/session ownership, and anonymous session isolation.
- Policy evaluation remains in-process, fast, and testable.
- Policy core composes with the Phase 19 permit/filter/repository architecture instead of bypassing it.
- Event contracts are append-only and support derived reads.
- Event envelope and projection/migration rules are sufficient to represent current turn, telemetry, retry, and tool-call behavior.
- Gateway code stays thin and does not accrete business policy.
- The first adopter path proves the design without forcing a premature micro-service.
