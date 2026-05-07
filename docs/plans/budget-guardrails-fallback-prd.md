# PRD: Budget Guardrails and Graceful Fallback Routing

## Overview

Implement spend guardrails that enforce per-request, per-session, and daily budgets with graceful model fallback routing that preserves quality while keeping costs predictable.

## Problem Statement

Without hard budget controls, agent orchestration can produce unpredictable spend spikes. Teams need deterministic budget enforcement that degrades gracefully instead of failing abruptly.

## Goals

- Prevent budget overruns at request, session, and day scopes.
- Degrade gracefully to lower-cost routes before hard failure.
- Preserve acceptable response quality while reducing spend.
- Provide transparent budget and fallback reasoning to operators.

## Non-Goals (v1)

- Cross-organization quota sharing.
- Billing reconciliation with external provider invoices.
- Dynamic market-price optimization per token in real time.

## User Stories

- As an admin, I set cost limits by scope and priority tier.
- As an operator, I can see why a fallback occurred.
- As a user, I still receive a useful response when expensive models are unavailable.
- As finance owner, I get alerts before daily budget exhaustion.

## Functional Requirements

### FR-1 Budget Profiles

- Define profiles by environment or route:
  - `dev`, `homelab`, `team-prod`
- Each profile sets:
  - per-request ceiling
  - per-session ceiling
  - daily paid budget cap
  - emergency reserve for critical priority

### FR-2 Budget Checks

- Preflight check before first model call.
- In-flight check before each fallback escalation.
- Hard block when all allowed budgets are exhausted.

### FR-3 Graceful Fallback Ladder

Fallback order:

1. Free-tier equivalent route (same task class)
2. Lower-cost paid model in same capability band
3. Compressed-context retry
4. Summarize-and-continue mode
5. Budget exhaustion response with actionable guidance

### FR-4 Priority-Aware Exceptions

- `critical` requests may use emergency reserve.
- Reserve usage is logged and alerting is immediate.

### FR-5 Quality Guardrails During Fallback

- If fallback output fails quality gate, attempt one constrained retry.
- Do not exceed max attempts and max spend for the scope.

### FR-6 Operator Visibility

- Emit fallback reason codes:
  - `budget_soft_limit`
  - `budget_hard_limit`
  - `provider_unavailable`
  - `quality_gate_failure`
- Show budget status and reserve consumption in UI.

## Non-Functional Requirements

- Budget decision overhead <= 20ms p95.
- Budget state consistency under concurrent requests.
- No negative remaining budget values due to race conditions.

## Architecture

| Component | Responsibility |
| --------- | -------------- |
| `BudgetPolicyEngine` | Resolves active profile and limits |
| `BudgetLedger` | Tracks spend by scope and time window |
| `FallbackPlanner` | Chooses next route/model under constraints |
| `BudgetGuardMiddleware` | Enforces checks preflight and in-flight |
| `BudgetAlertService` | Threshold and reserve notifications |

## Data Model

### Budget Profile

```json
{
  "name": "team-prod",
  "requestCapUsd": 0.08,
  "sessionCapUsd": 1.25,
  "dailyPaidCapUsd": 50.0,
  "criticalReserveUsd": 5.0,
  "maxAttempts": 3
}
```

### Spend Ledger Entry

```json
{
  "timestamp": "2026-05-07T12:10:00Z",
  "requestId": "req_789",
  "sessionId": "s_123",
  "route": "large",
  "provider": "azure",
  "estimatedUsd": 0.021,
  "scope": "daily_paid",
  "remainingAfterUsd": 49.979
}
```

## API Requirements

| Method | Endpoint | Purpose |
| ------ | -------- | ------- |
| GET | `/api/budget/status` | Current usage and remaining by scope |
| GET | `/api/budget/profiles` | List budget profiles |
| PUT | `/api/budget/profiles/{name}` | Upsert profile |
| GET | `/api/budget/events` | Fallback and budget decision events |

## UI Requirements

- Settings page: budget profile editor and simulation widget.
- Diagnostics page: per-run fallback chain and budget decisions.
- Dashboard card: daily budget gauge + reserve consumption.

## Security and Governance

- Budget profile modifications require admin role.
- Signed audit records for profile changes.
- No client-side trust for budget calculations.

## Telemetry and Alerts

Track:

- `budget_remaining_usd` by scope.
- `fallback_invocations_total` by reason.
- `critical_reserve_used_usd` per day.
- `budget_blocked_requests_total`.

Alerts:

- 80% daily budget used (warning).
- 100% daily budget used (critical).
- Any critical reserve usage (warning).

## Rollout Plan

1. Phase 0: ledger in observe-only mode.
2. Phase 1: soft guardrails + alerting.
3. Phase 2: hard guardrails + fallback ladder.
4. Phase 3: reserve and priority exceptions.

## Acceptance Criteria

- AC-1: Hard caps are never exceeded under concurrent load tests.
- AC-2: 95% of budget-triggered requests return degraded but useful output instead of failure.
- AC-3: All fallback paths include reason code and spend delta in logs.
- AC-4: Budget profile updates take effect without service restart.
- AC-5: Dashboard and API budget totals reconcile within 1%.

## Dependencies

- Model routing and quality gate pipeline.
- Token usage metadata from provider responses.
- Admin settings infrastructure.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Excessive degradation harms UX | Quality gate and capped retry before final degrade |
| Ledger race conditions | Atomic updates and optimistic concurrency controls |
| Misconfigured caps block too much traffic | Profile simulation and dry-run validation |

## Open Questions

- Should caps be configured in USD only or also token counts?
- Do we need per-user caps in single-admin mode?
- Should reserve be split by route tier?

## Implementation Clarifications (v1 Defaults)

Use these defaults unless finance or routing review overrides them during grooming:

- Budgets are enforced in USD in v1. Token counts may be logged for diagnostics but do not participate in hard enforcement.
- Scope evaluation order is request, then session, then daily budget. The first violated scope determines the primary fallback or block reason.
- Fallback ladders are static per route/profile and versioned in config. v1 does not do real-time provider price arbitrage.
- Emergency reserve can only be consumed by requests explicitly tagged `critical` through trusted server-side policy, never by client input alone.
- When all allowed routes are exhausted, the system returns a structured degraded response with the budget reason code and next-step guidance.

## Sprint-Ready Engineering Tickets

- [ ] `BGT-01` Define budget profile, spend ledger, and reason-code contracts, including reconciliation fields for estimated versus actual spend.
- [ ] `BGT-02` Implement `BudgetPolicyEngine` and atomic `BudgetLedger` updates with concurrency tests that prove caps are never exceeded under parallel load.
- [ ] `BGT-03` Build the static fallback planner for free-tier, lower-cost, compressed-context, summarize-and-continue, and hard-stop paths per profile.
- [ ] `BGT-04` Integrate preflight and in-flight budget checks into the model routing pipeline so every attempt records spend deltas, fallback reasons, and reserve usage.
- [ ] `BGT-05` Add admin APIs and UI for profile editing, simulation, daily budget status, reserve consumption, and per-run fallback inspection.
- [ ] `BGT-06` Emit telemetry and alerts for 80 percent usage, 100 percent usage, reserve consumption, and blocked requests, and verify alert fan-out in staging.
- [ ] `BGT-07` Add integration and soak coverage for hard-cap enforcement, quality-gated constrained retries, fallback usefulness, and dashboard/API reconciliation within the 1 percent target.
