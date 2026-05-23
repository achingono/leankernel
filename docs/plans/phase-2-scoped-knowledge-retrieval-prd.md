# PRD: Phase 2 Scoped Knowledge Retrieval

## Overview

Implement deterministic scoped knowledge retrieval for LeanKernel Phase 2 so context assembly can enforce retrieval boundaries, perform bounded entity-aware expansion, and emit per-candidate retrieval diagnostics.

## Problem Statement

LeanKernel can already retrieve knowledge candidates through `IKnowledgeService`, but Phase 1 retrieval is unscoped and opaque. The runtime currently admits knowledge without explicit policy enforcement, bounded entity expansion, or auditable retrieval diagnostics. That creates leakage risk for sensitive namespaces, reduces relevance when related entities are buried behind indirect references, and makes it hard to inspect why candidates were included or excluded.

## Goals

- Enforce deterministic retrieval scope policies before knowledge reaches prompt assembly.
- Add bounded entity-aware expansion and score boosting without changing `LeanKernel.Knowledge`.
- Emit diagnostics for every candidate considered, including exclusions.
- Preserve a no-fallback contract when scoped retrieval finds no admissible results.
- Keep the integration feature-local to `LeanKernel.Abstractions`, `LeanKernel.Context`, configuration, and tests.

## Non-Goals

- Changes inside `LeanKernel.Knowledge` or the GBrain transport contract.
- New diagnostics API endpoints beyond surfacing retrieval diagnostics in the retrieval result path.
- Heuristic scope inference from channel names or undocumented key naming conventions.
- Silent fallback from scoped retrieval to global retrieval when scoping is enabled.

## Reviewed Design Decisions

This plan incorporates independent review feedback before implementation.

- **Fail closed for missing policies**: when scoping is enabled, unknown scopes resolve to the configured default policy; if no matching policy exists, scoped retrieval returns an empty result set with diagnostics rather than a permissive fallback.
- **Deterministic scope precedence**: request metadata keys are resolved in the order `retrieval_scope` -> `task_scope` -> `agent_scope` -> configured default scope.
- **No undocumented heuristics**: namespace checks rely on candidate metadata (for example `namespace`) and explicit policy configuration, not on channel-name shortcuts or inferred key prefixes unless metadata is unavailable and the key contains a stable namespace segment before `/` or `:`.
- **Scoped retrieval owns scope logic**: `ContextCandidateRetriever` only decides whether scoping is enabled; `IScopedKnowledgeService` handles policy resolution, entity expansion, score adjustment, and diagnostics.
- **Preserve source scores**: diagnostics record both the original backend score and the adjusted score after entity-aware boosting.
- **DI lifetime correction**: while touching registrations, align `ContextCandidateRetriever` and `IContextGatekeeper` with the scoped `ISessionStore` dependency to avoid singleton-to-scoped lifetime mismatch.

## Functional Requirements

### FR-1 Configuration

Add `RetrievalConfig` and `ScopePolicyDefinition` under `LeanKernel.Abstractions.Configuration`, and expose `LeanKernelConfig.Retrieval`.

Required settings:

- `ScopingEnabled`
- `DefaultScope`
- `MaxEntityExpansionResults`
- `EntityBoostMultiplier`
- `MinScopeRelevanceScore`
- `EmitRetrievalDiagnostics`
- `ScopePolicies`

### FR-2 Scope Resolution

Add `RetrievalScopePolicy` in `LeanKernel.Context/Retrieval`.

Requirements:

- Resolve the effective scope from optional message metadata and configuration.
- Resolve the matching `ScopePolicyDefinition` deterministically.
- Use `DefaultScope` when metadata does not specify a scope.
- Return a stable result for identical inputs.
- Do not fall back to an unrestricted scope when a policy is missing.

### FR-3 Scoped Retrieval Execution

Add `IScopedKnowledgeService` and implement it via `ScopedKnowledgeService`.

Requirements:

1. Call `IKnowledgeService.SearchAsync` with the incoming query.
2. Resolve the effective scope and applicable scope policy.
3. Apply namespace include/exclude rules.
4. Enforce required metadata keys.
5. Apply bounded entity-aware boosting.
6. Apply minimum score thresholds from both global retrieval config and scope policy.
7. Return only admitted candidates, sorted deterministically.
8. Emit diagnostics for every candidate considered.
9. If no candidates are admitted, return an empty candidate set plus diagnostics; do not silently widen scope.

### FR-4 Entity Expansion

Add `EntityExpander` in `LeanKernel.Context/Retrieval`.

Requirements:

- Extract deterministic seed entities from the query and top retrieval candidates.
- Use `IKnowledgeService.SearchAsync` for bounded related-entity expansion.
- Respect `EntityExpansionDepth` and `MaxEntityExpansionResults`.
- Deduplicate expansions and avoid unbounded traversal loops.
- Return both expanded entity terms and a set of candidate keys eligible for boosting.

### FR-5 Diagnostics

Add `RetrievalDiagnostics` and `RetrievalCandidateDecision` under `LeanKernel.Abstractions.Models`.

Requirements:

- Diagnostics include `SessionId`, `TurnId`, effective scope, expanded entities, totals, and one decision per considered candidate.
- Every decision records key, source, original score, adjusted score, admission status, and optional exclusion reason.
- Exclusion reasons must distinguish scope exclusion and score exclusion at minimum.

### FR-6 Context Integration

Update `ContextCandidateRetriever`.

Requirements:

- Blank messages still skip knowledge retrieval.
- When scoping is enabled, route retrieval through `IScopedKnowledgeService`.
- When scoping is disabled, continue to call `IKnowledgeService.SearchAsync` directly.
- Preserve history retrieval behavior.
- Surface retrieval diagnostics on the retriever result for downstream consumers.

### FR-7 Dependency Injection and Configuration Binding

Update `ContextServiceCollectionExtensions` and startup wiring.

Requirements:

- Register `IScopedKnowledgeService`, `RetrievalScopePolicy`, and `EntityExpander`.
- Bind both `ContextConfig` and `RetrievalConfig` through options registration.
- Correct scoped lifetimes for services that depend on `ISessionStore`.
- Update gateway startup to pass retrieval configuration into context registration.

### FR-8 Tests

Add unit coverage under `src/LeanKernel.Tests.Unit/Context/Retrieval/`.

Required test areas:

- scope precedence and fallback behavior
- unknown scope or missing default-policy behavior
- namespace include/exclude filtering
- required metadata enforcement
- score threshold enforcement
- entity expansion depth bounding and deduplication
- entity-aware boosting and stable sorting
- diagnostics totals and per-candidate decisions
- `ContextCandidateRetriever` enabled/disabled scoping branches
- blank-message behavior remains unchanged

## Implementation Notes

- Add optional request metadata to `LeanKernelMessage` so retrieval scope overlays can be supplied deterministically.
- Prefer XML documentation on new public abstractions and models.
- Use primary constructors where they fit existing project style and keep argument validation explicit.
- Keep retrieval behavior deterministic by sorting admitted results by adjusted score descending, then source, then key.

## Configuration Example

```json
"Retrieval": {
  "ScopingEnabled": true,
  "DefaultScope": "global",
  "MaxEntityExpansionResults": 5,
  "EntityBoostMultiplier": 1.5,
  "MinScopeRelevanceScore": 0.3,
  "EmitRetrievalDiagnostics": true,
  "ScopePolicies": [
    {
      "Name": "global",
      "IncludeNamespaces": [],
      "ExcludeNamespaces": ["identity"],
      "RequiredMetadataKeys": [],
      "MinScore": 0.0
    },
    {
      "Name": "personal",
      "IncludeNamespaces": ["identity", "preferences"],
      "ExcludeNamespaces": [],
      "RequiredMetadataKeys": [],
      "MinScore": 0.2
    }
  ]
}
```

## Acceptance Criteria

- AC-1: A candidate in an excluded namespace is omitted with a scope exclusion reason in diagnostics.
- AC-2: A candidate missing a required metadata key is omitted deterministically.
- AC-3: Entity expansion boosts matching candidates and diagnostics preserve original and adjusted scores.
- AC-4: Scoped retrieval does not fall back to global retrieval when no candidates pass policy.
- AC-5: `ContextCandidateRetriever` uses scoped retrieval only when `ScopingEnabled` is true.
- AC-6: Retrieval diagnostics capture every candidate considered, not only admitted ones.
- AC-7: Context service registrations no longer create a singleton-to-scoped lifetime mismatch.

## Validation Plan

- Add and review unit tests for retrieval scope policy, scoped knowledge service, entity expansion, and retriever integration.
- Attempt repository validation commands where available.
- If `dotnet` is unavailable in the execution environment, record that validation limitation explicitly in the final report.
