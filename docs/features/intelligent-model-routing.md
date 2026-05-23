# Intelligent Model Routing

This document reflects the current implementation in `LeanKernel.Thinker/Routing`.

## Components

| Component | Responsibility |
| --- | --- |
| `TaskComplexityScorer` | Classifies request as `small`, `medium`, `large` from estimated tokens + constraint count |
| `PolicyModelSelector` | Builds ordered candidate chain (free-first, optional paid fallback) |
| `ResponseQualityGate` | Applies deterministic quality checks to trigger escalation when enabled |
| `ProviderHealthTracker` | Cooldown tracking for failing aliases |
| `SpendGuard` | Daily paid-request soft/hard threshold guard |
| `ModelRoutingService` | End-to-end orchestration and structured selection metadata |
| `SelectionLogStore` | In-memory ring buffer (10,000 entries) for routing observability |

## Behavior Flags and Defaults

Routing config lives under `LeanKernel:Routing`:

| Field | Default |
| --- | --- |
| `Enabled` | `false` |
| `ShadowMode` | `true` |
| `EnableQualityEscalation` | `false` |
| `SmallMaxTokens` | `4000` |
| `SmallMaxConstraints` | `3` |
| `MediumMaxTokens` | `16000` |
| `MediumMaxConstraints` | `8` |
| `SmallAlias` / `MediumAlias` / `LargeAlias` | `small` / `medium` / `large` |
| `CooldownSeconds` | `60` |
| `MaxProviderAttempts` | `3` |
| `MaxSelectionBudgetMs` | `30000` |
| `QualityMinOutputLength` | `80` |
| `QualityMinConstraintCoverage` | `0.80` |

Spend guard:

- `SpendGuard.DailyPaidRequestSoftLimit` (0 disables)
- `SpendGuard.DailyPaidRequestHardLimit` (0 disables)

## Routing Flow

1. Score request complexity.
2. Build candidate aliases by complexity tier.
3. Skip aliases currently on cooldown.
4. Invoke candidate via `AgentFactory.CreateAgentForModel(alias, ...)`.
5. If `EnableQualityEscalation=true`, run response quality gate and escalate if failed.
6. Stop when response accepted, attempts exhausted, or time budget reached.
7. Record structured selection metadata and optional paid usage count.

## Quality Gate Checks

`ResponseQualityGate` currently checks:

1. Non-empty output
2. Minimum output length (`QualityMinOutputLength`) unless prompt is explicitly terse
3. Constraint-coverage heuristic for prompts with at least 4 detected constraints

## Failure / Cooldown Handling

Transient failures trigger fallback and cooldown when status resembles:

- `429`
- `5xx` (`500`, `502`, `503`, `504`)

Cooldown duration is `Routing.CooldownSeconds`.

## Shadow Mode

When `Enabled=true` and `ShadowMode=true`, routing logic executes for diagnostics but does not affect the actual response sent to the user. Instead, the normal (non-routing) response path in Thinker remains authoritative—final response selection is made by the default strategy, not by the routing logic output. This allows safe evaluation of routing decisions without impacting user experience.
