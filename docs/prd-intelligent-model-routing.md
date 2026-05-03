# PRD: Intelligent Cost-Quality Model Routing

## 1. Overview

LeanKernel currently uses static LiteLLM route ordering with fallback. This works for availability but does not fully optimize quality-per-cost in real time. This PRD defines an intelligent routing policy that:

- Prioritizes free models by default.
- Preserves answer quality by selecting the most capable model required for the task.
- Falls back to paid models only when free providers are rate-limited, unavailable, or fail quality gates.
- Continuously syncs provider limits (context window and output limits) from deployed model metadata.

This PRD is planning-only. No implementation is included in this document.

## 2. Goals

- **Quality-first under budget constraints**: choose the strongest suitable model while avoiding unnecessary paid usage.
- **Free-first policy**: prefer free providers before paid fallback.
- **Task-aware model selection**: route by complexity, context size, and requested output characteristics.
- **Automated config hygiene**: sync deployed model limits into [config/litellm/config.yaml](config/litellm/config.yaml).
- **Operational resilience**: automatic escalation/fallback on 429/5xx and quality failures.
- **Traceability**: emit structured logs/metrics explaining why a model was selected.

## 3. Non-Goals (v1)

- Per-end-user personalized model preferences.
- Fine-grained billing attribution beyond provider-level spend.
- Full reinforcement-learning optimizer.
- Human-in-the-loop quality labeling pipeline.

---

## 4. Provider Matrix and Policy

### 4.1 Provider Universe (v1)

- **Groq**: free, low context window, fast.
- **Gemini**: free, high context window, aggressive rate limits.
- **Azure**: free via monthly credits (rate-limited) and treated as `free` until monthly credit budget is exhausted.
  - Deployed models include `gpt-5.4-1`, `gpt-5.4-mini-1`, `gpt-5.4-nano-1`, `kimi-k2.6`, `DeepSeek-V3.2`.
  - Kimi and DeepSeek are Azure deployments (same cost bucket as Azure), not separate providers.
- **GitHub Copilot**: paid credits, fallback only unless escalation policy requires it.

### 4.2 Policy Defaults

- Default order: free candidates first, paid candidate last.
- Paid provider can be selected only when:
  - All free candidates fail fit/availability/quality checks, or
  - Request priority is explicitly marked `critical`.
- Kimi and DeepSeek must be present in active route chains (not only provider catalog).

---

## 5. Functional Requirements

### FR-1: Task Complexity Classification

The system shall classify each request into `small`, `medium`, or `large` before model invocation.

For v1, a `constraint` is any distinct imperative requirement extracted from the user message, including:

- explicit numbered/listed requirements
- hard output format requirements (for example: JSON schema, fixed sections)
- required tools/providers/models
- mandatory inclusions/exclusions

Thresholds for v1:

- `small`: estimated input <= 4,000 tokens and <= 3 constraints.
- `medium`: estimated input 4,001-16,000 tokens or 4-8 constraints.
- `large`: estimated input > 16,000 tokens, or > 8 constraints, or multi-step generation expected.

### FR-2: Capability/Fit Gate

Before invoking a model, the selector shall validate:

- Input fits model `context_window`.
- Requested output fits model `max_tokens`.
- Required modality/features are supported.

If any check fails, skip to next candidate.

### FR-3: Free-First with Quality Preservation

For each complexity tier, candidate order is:

1. Free candidate(s) in same tier.
2. Free candidate(s) in adjacent tier.
3. Paid fallback candidate(s).

### FR-4: Quality Gate and Escalation

After a response is generated, apply deterministic quality checks:

- Non-empty output (`trimmed length > 0`).
- Minimum useful output length for non-trivial tasks (`>= 80 chars` unless prompt asks for terse output).
- Constraint coverage (`>= 80%` of explicitly enumerated constraints detected in output for medium/large).

If gate fails, retry next candidate in graph.

### FR-5: Failure and Rate-Limit Handling

On status codes `429, 500, 502, 503, 504`:

- Mark deployment as cooled down for `60s` (configurable).
- Retry with next candidate.
- Enforce escalation budget:
  - Max provider attempts per request: `3`.
  - Max end-to-end selection time budget: `30s`.

### FR-6: Deployed Limit Sync

A sync utility shall update chat model limits in [config/litellm/config.yaml](config/litellm/config.yaml) from live metadata.

Metadata sources for v1:

- Gemini: `https://generativelanguage.googleapis.com/v1beta/models`
- Groq: `https://api.groq.com/openai/v1/models`
- Azure: `{AZURE_AI_API_BASE}/openai/v1/models` and `{AZURE_AI_API_BASE_2}/openai/v1/models` (try all configured key/base pairs)

Fields to sync:

- `context_window`
- `max_tokens`

Sync behavior:

- Dry-run mode.
- Write mode.
- Best-effort per provider (partial failure does not abort entire run).
- Drift report output per model field change.

### FR-7: Explainability and Metrics

Per request, emit structured selection metadata:

- `request_id`
- selected `route`, `alias`, `provider`, `model`
- selection reason
- fallback path and retry count
- provider outcome (success/error/429)
- latency and token usage
- estimated and actual cost bucket (`free` or `paid`)

### FR-8: Spend Guardrail

When paid spend exceeds configured thresholds:

- Soft threshold: emit warning alerts.
- Hard threshold: disable paid fallback except `critical` priority requests.

---

## 6. Route and Alias Mapping Requirements

### 6.1 Tier Mapping

Maintain explicit tier intent:

- `small` -> `gpt-5.4-nano-1`
- `medium` -> `gpt-5.4-mini-1`
- `large` -> `gpt-5.4-1`

### 6.2 Required Route Inclusion

`kimi-k2.6` and `DeepSeek-V3.2` must remain included in active route chains across all tiers as free candidates/fallbacks.

### 6.3 Alias Consistency

Aliases must map consistently:

- `gpt-5.4-nano` -> `small`
- `gpt-5.4-mini` -> `medium`
- `gpt-5.4` -> `large`

---

## 7. Proposed Architecture

### 7.1 Components

- **TaskComplexityScorer**: classifies request tier.
- **PolicyModelSelector**: ranks candidates by free-first and fit checks.
- **ResponseQualityGate**: validates response quality and triggers escalation.
- **ProviderHealthTracker**: tracks cooldowns/rate limits.
- **SpendGuard**: enforces paid spend thresholds.
- **ModelLimitSyncService**: syncs model limits from provider metadata.

### 7.2 Integration Points

- Selection path: [src/LeanKernel.Thinker/ThinkerService.cs](src/LeanKernel.Thinker/ThinkerService.cs)
- Routing source: [config/litellm/config.yaml](config/litellm/config.yaml)
- Config compiler: [config/render_litellm_config.py](config/render_litellm_config.py)
- Sync utility: [scripts/sync_litellm_model_limits.py](scripts/sync_litellm_model_limits.py)

---

## 8. Observability Dashboard

### 8.1 Purpose

A live dashboard provides the primary mechanism for validating acceptance criteria across rollout phases, detecting regressions, and monitoring ongoing cost-quality balance.

### 8.2 Dashboard Panels

| Panel | Metric | Visualization | AC Reference |
|---|---|---|---|
| Free vs. Paid Usage | % of requests served by free providers (7-day rolling) | Line chart with 90% target line | AC-1, AC-2 |
| Escalation Rate by Tier | Escalation events per tier (`small`, `medium`, `large`) per hour | Stacked bar chart | AC-4 |
| Latency Percentiles | p50 / p95 / p99 per active route (rolling 1h and 24h) | Multi-line time series | AC-3 |
| Error / 429 Rate by Provider | 429, 5xx rate per provider per hour | Grouped bar chart | FR-5 |
| Cost-per-Request Trend | Rolling average estimated cost bucket (`free` / `paid`) over time | Area chart with baseline overlay | AC-2 |
| Complexity Distribution | % classified `small` / `medium` / `large` per hour | Stacked area chart | FR-1 |
| Provider Health / Cooldown Status | Current cooldown state per deployment | Status table (green/red/orange) | FR-5 |
| Spend Guard Threshold | Current paid spend vs. soft and hard thresholds | Gauge / progress bar | FR-8 |
| Selection Reason Breakdown | Top reasons for model selection (free-first, fit-gate, escalation, paid-fallback) | Pie / donut chart | AC-8 |

### 8.3 Data Source

All panels consume structured JSON selection logs emitted by FR-7. The log fields required per panel are:

- `provider`, `model`, `cost_bucket` → Free vs. Paid Usage, Cost-per-Request
- `route`, `tier`, `escalation_count` → Escalation Rate
- `latency_ms`, `route` → Latency Percentiles
- `provider_outcome`, `provider` → Error / 429 Rate
- `complexity_class` → Complexity Distribution
- `provider_health_snapshot` → Provider Health Status
- `paid_spend_usd` → Spend Guard
- `selection_reason` → Selection Reason Breakdown

### 8.4 Tooling

- **Primary option**: Grafana + Loki (log-based metrics). Loki parses JSON selection logs; Grafana renders panels. Suitable for containerized deployment alongside existing Docker Compose stack.
- **Alternative**: Blazor admin panel embedded in `LeanKernel.Host` (a `/admin/routing` route). Preferable if external observability tooling is not available, using in-memory ring-buffer or SQLite backend for dashboard queries.
- The chosen tooling must be operational at the start of Phase 1 (before any live traffic change).

### 8.5 Alert Thresholds

| Alert | Condition | Severity |
|---|---|---|
| Free usage below target | Free provider rate < 85% over 1-hour window | Warning |
| Free usage critical | Free provider rate < 70% over 30-minute window | Critical |
| Paid spend soft limit | Paid spend reaches 80% of configured daily USD cap | Warning |
| Paid spend hard limit | Paid spend reaches 100% of configured daily USD cap | Critical (disable paid fallback) |
| Latency spike | p95 latency increase >= 30% vs baseline for 15 min | Warning |
| Success rate drop | Success rate drop >= 3% vs baseline for 30 min | Critical (trigger rollback) |
| Provider unhealthy | Any provider in cooldown for >= 10 consecutive minutes | Warning |

### 8.6 Dashboard Acceptance Gate

Dashboard must be operational and rendering live data before Phase 1 exits dry-run mode. Specifically:

- Free vs. Paid Usage panel must display >= 7 days of baseline data.
- All alert rules must be configured and tested with synthetic events.
- Dashboard access must not require production credentials.

---

## 9. Rollout Plan

### Phase 0: Baseline and Definitions

- Deploy observability dashboard (Section 8) and confirm all panels render live data.
- Capture baseline metrics from static routing (7-day window):
  - paid usage rate
  - success rate
  - p50/p95/p99 latency
  - fallback rate
- Finalize complexity thresholds and quality checks.

### Phase 1: Dry-Run Selector (No Traffic Impact)

- Run selector in shadow mode (decision logs only).
- Continue serving with existing static routes.
- **Dashboard gate**: Free vs. Paid Usage panel must show >= 7-day baseline and all alerts must be active before exiting this phase.
- Exit criteria: shadow selector reaches >= 85% agreement with accepted outcomes.

### Phase 2: Selector Live, No Quality Escalation

- Enable selector for primary model choice.
- Keep escalation disabled.
- Monitor cost/latency drift.

### Phase 3: Quality Escalation + Spend Guard

- Enable deterministic quality gate and bounded escalation.
- Enable spend guardrails and paid circuit-breaker.

### Phase 4: Sync Automation and Tuning

- Run limit sync in CI/scheduled workflow.
- Tune thresholds based on observed outcomes.

### Rollback Criteria

Automatically rollback to static route selection when either condition is true for > 30 minutes:

- success rate drops by >= 3% vs baseline.
- p95 latency increases by >= 30% vs baseline.

---

## 10. Acceptance Criteria

- **AC-1**: For requests classified `small` or `medium`, free provider selected first in >= 90% of requests over a rolling 7-day window and >= 1,000 requests.
- **AC-2**: Paid usage rate decreases by >= 25% vs baseline, while success rate change is within +/-1%.
- **AC-3**: p95 latency increase is <= 15% vs baseline.
- **AC-4**: Quality-gate escalation triggers for empty/low-coverage responses and remains within max 3 attempts.
- **AC-5**: Tier aliases resolve to intended deployments (`small`/`medium`/`large`).
- **AC-6**: Kimi and DeepSeek are present in active route chains across all tiers.
- **AC-7**: Sync utility supports dry-run and write mode and reports field-level diffs.
- **AC-8**: Selection logs include request_id, provider/model choice, reason, fallback path, and cost bucket.
- **AC-9**: Observability dashboard is operational before Phase 1 exits, renders all nine KPI panels with live data, and all alert rules are active.

---

## 11. Risks and Mitigations

- **Risk**: Provider metadata incomplete/inconsistent.
  - **Mitigation**: best-effort sync, conservative defaults, dry-run diff review.
- **Risk**: Misclassification causes poor model choices.
  - **Mitigation**: shadow mode and threshold tuning before live activation.
- **Risk**: Over-escalation increases cost/latency.
  - **Mitigation**: strict attempt/time budgets and spend guard.
- **Risk**: Rate-limit storms.
  - **Mitigation**: cooldown tracker, cross-provider fallback, circuit breaker.
- **Risk**: Silent regression after rollout.
  - **Mitigation**: explicit rollback criteria and alerting.

---

## 12. Open Questions

- Should `critical` priority be API-exposed in v1 or internal-only?
- Should paid fallback be disabled entirely in non-production by default?
- Should per-team or per-session budget caps be added in v2?

---

## 13. Out of Scope Implementation Notes

Implementation should begin only after PRD sign-off. This document defines requirements, metrics, and rollout safety gates only.
