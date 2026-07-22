# Phase 20 - Identity, Policy, And Event Spine

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)
- [Addendum](addendum.md)

## Objective

Establish the architectural foundations that future LeanKernel phases will build on: a canonical identity model, a shared in-process policy core, and an append-only event spine for turns, tool calls, and telemetry. This phase reduces later churn by freezing the core contracts that currently leak across gateway, logic, memory, authorization, and telemetry code paths, while preserving the distinct identity and persistence invariants that already exist in the runtime.

## Scope

This phase creates a shared library-first policy core, defines canonical identity and event contracts, and introduces a stable event capture/persistence shape that downstream features can query without reinterpreting ad-hoc state. It also adds guardrails so the Gateway stays thin and host-specific concerns remain out of the core. The design must preserve the current split between memory identity (`tenant/person/channel`), transcript/session identity (`tenant/user/channel`), and anonymous session isolation (`tenant/channel/user/session`) rather than collapsing them into one partition key.

## In Scope

- Canonical identity model and invariants for tenant, person, user, channel, and anonymous-session boundaries, including where each dimension is authoritative.
- Shared `IPolicyContext`, `IPolicy<TEntity>`, and `IPolicyEvaluator` abstractions in a reusable library that compose with the existing `IPermit<TEntity>`, `IFilter<TEntity>`, and `IRepository<TEntity>` pipeline instead of creating a parallel authorization path.
- Default policy implementations for identity linking, memory access, authorization gating, and budget checks, with clear ownership boundaries between repository enforcement and higher-level domain decisions.
- Append-only event contracts for turns, tool calls, and telemetry with a derived-read orientation, explicit event envelope metadata, and a migration path from the current `TurnEntity`/`TurnTelemetryEntity` persistence model.
- Gateway composition rules that keep transport and host concerns at the edge.
- Contract tests and documentation for the new core surfaces.

## Out of Scope

- Extracting the policy core into a separate micro-service.
- Rebuilding channel connectors, UI, or model-routing behavior.
- Reworking existing feature behavior beyond the first adopter migrations required to prove the new core.
- Any cross-tenant identity linking.

## Entry Criteria

- Existing identity, channel, memory, telemetry, and permit behavior is documented in current phase plans and feature docs.
- Policy-sensitive consumers already exist and can be migrated incrementally.
- The repository has an agreed preference for in-process shared policy code before any service split.

## Exit Criteria

The canonical identity model, shared policy core, and event spine are defined, implemented, and adopted by at least one real consumer path. Gateway business logic remains thin, policy decisions are testable in-process, the existing permit/filter/repository enforcement remains the only authorization/data-partition gate, and the new contracts are documented for subsequent phases. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
