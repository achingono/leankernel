# PRD: Phase 3 Reliability and Optimization

**Document Type:** Product Requirements Document  
**Audience:** LeanKernel platform engineers, architects, and maintainers  
**Goal:** Define the backend and API requirements for Phase 3 of the LeanKernel rearchitecture so the platform can deliver reliable routed execution, auditable quality controls, multi-agent workflows, asynchronous learning, proactive runtime behavior, and production-ready operations.

## Executive Summary

Phase 3 turns LeanKernel from a Phase 2-capable personal agent into a production-ready reliability platform.

Phase 2 is assumed complete and stable: identity grounding, scoped retrieval, deterministic history shaping, and channel delivery are already working in production. Phase 3 builds on that foundation to add model routing, deterministic quality gates, escalation, shadow routing, multi-agent orchestration, synchronous response enhancement, post-turn learning, scheduled proactive execution, and production hardening.

This phase is **API and logic only**. It does not include UI or admin console work. The implementation must stay aligned with **Microsoft Agent Framework (MAF) v1.6.1** concepts wherever possible, especially for workflows, agent composition, and tool invocation.

## Problem Statement

LeanKernel can already assemble grounded context and respond across channels, but it still lacks the full reliability layer required for consistent production operation. Without routed model selection, deterministic quality validation, shadow evaluation, and production-grade fallback behavior, the system risks:

- inconsistent answer quality across request types
- unnecessary spend on overpowered models
- silent failures when providers or enrichment dependencies degrade
- limited visibility into why a model, worker, or enhancement path was chosen
- weak separation between foreground user response and background learning
- incomplete support for proactive and scheduled agent behavior

Phase 3 closes those gaps by making reliability, auditability, and operational safety first-class backend features.

## Goals

- Select the right model tier for each turn using deterministic routing inputs and policy.
- Escalate safely when the first response fails deterministic quality expectations.
- Run shadow routing experiments without affecting user-visible behavior.
- Support complex work through MAF-native multi-agent workflows and agent-as-tool composition.
- Enhance responses synchronously without violating safety, routing, or audit guarantees.
- Persist every completed turn and learn from it asynchronously.
- Execute proactive and scheduled tasks through durable runtime job definitions.
- Harden the stack for provider outages, spend limits, observability, and production operations.

## Non-Goals (v1)

- UI or admin console delivery.
- New channel implementations.
- Model training or fine-tuning.
- Human review workbenches for routing or learning outcomes.
- Cross-tenant policy systems beyond what Phase 2 already supports.
- Replacing Phase 2 identity, retrieval, or history-shaping behavior.

## Scope and Phase Dependencies

### Required Phase 2 Preconditions

Phase 3 assumes the following are already complete and production-ready:

- durable user and agent identity
- scoped retrieval and context gating
- deterministic history shaping
- channel routing and outbound delivery
- core API auth and session persistence

### Scope Boundaries

- Backend and API logic only.
- All policies must be enforceable server-side; client input cannot override routing, spend, or safety controls.
- Routing, enhancement, learning, and scheduler features must degrade safely when optional dependencies are unavailable.
- Configuration must preserve the existing LeanKernel binding style under `LeanKernel:*`.

## End-to-End Turn Lifecycle

Phase 3 should standardize the request lifecycle in this order:

1. API ingress assigns a correlation ID, run ID, and request policy context.
2. `ModelRouter` evaluates prompt complexity, context size, tool pressure, provider health, and budget state.
3. The primary execution path runs either as a single-agent turn or as a MAF workflow/coordinator path.
4. `ResponseQualityGate` evaluates the result deterministically.
5. `EscalationPolicy` retries with the next allowed tier when the gate fails or a recoverable provider issue occurs.
6. `ShadowRoutingStrategy` may run in parallel under sampling policy for evaluation only.
7. `IResponseEnhancer` applies synchronous enhancement steps before delivery.
8. The final response is returned to the caller.
9. `PostTurnPipeline` persists the assistant turn and emits a `TurnEvent`.
10. Background learning and scheduled jobs process follow-on work without extending user-visible latency.

## Architecture Mapping

| Module | Phase 3 responsibilities |
| ------ | ------------------------ |
| `LeanKernel.Core` | Contracts, config models, result records, event payloads, worker/job definitions |
| `LeanKernel.Thinker` | Routing, quality gates, escalation, workflows, enhancers, post-turn pipeline, learning worker |
| `LeanKernel.Archivist` | Wiki extraction, retrieval-backed enhancement inputs, engagement artifact services |
| `LeanKernel.Plugins` | Worker tools, agent-as-tool adapters, scoped tool definitions |
| `LeanKernel.Scheduler` | Cron scheduler, time-boundary service, job dispatcher, proactive execution runtime |
| `LeanKernel.Commander` | Channel-safe proactive delivery and durable outbound execution |
| `LeanKernel.Host` | API composition, rate limiting, tracing, health endpoints, DI wiring |

## Functional Requirements

### FR-1: Model Routing and Escalation

LeanKernel must route each turn to the lowest-cost model that can reasonably satisfy the request while preserving deterministic escalation when the initial attempt is insufficient.

#### Requirements

- Introduce `ModelRouter` as the public routing façade for turn execution.
- `TaskComplexityScorer` must estimate required model tier from:
  - prompt complexity
  - estimated total context size
  - explicit constraints and structure requirements
  - expected tool usage or tool count
  - task sensitivity or quality risk indicators
- `PolicyModelSelector` must map complexity plus active cost policy to an ordered candidate ladder.
- Candidate selection must also consider:
  - provider health/cooldown state
  - spend guard state
  - max context window compatibility
  - tool capability requirements
- `EscalationPolicy` must retry with the next allowed tier when:
  - the quality gate fails
  - the provider returns a transient failure
  - the selected route cannot satisfy required tools or context limits
- Routing must stop when any configured guardrail is hit:
  - max attempts
  - max routing time budget
  - hard spend limit
  - no healthy candidates remain
- Routing diagnostics must record:
  - selected model and tier
  - reason code
  - alternatives considered and why they were rejected
  - provider health snapshot
  - budget snapshot
  - escalation chain
  - final outcome
- Routing decisions must be deterministic for the same input, configuration version, health snapshot, and budget state.

#### Components

| Component | Responsibility |
| --------- | -------------- |
| `ModelRouter` | Orchestrates route selection, execution attempts, and escalation |
| `TaskComplexityScorer` | Produces complexity score, estimated tokens, and constraint density |
| `PolicyModelSelector` | Builds the ordered candidate ladder from complexity and policy |
| `EscalationPolicy` | Applies retry, stop, and next-tier rules |
| `ProviderHealthTracker` | Exposes route availability and cooldown/circuit state |
| `SpendGuard` | Enforces per-session/day/month budget eligibility |
| `RoutingDiagnosticsWriter` | Persists structured routing audit records |

#### Acceptance Criteria

- The same turn under the same routing inputs produces the same selected candidate and reason code.
- Every routed turn emits a structured routing record, including rejected alternatives.
- Quality-gate or transient-provider retries follow the configured ladder and never exceed configured attempt limits.
- Hard spend or provider-health blocks result in a structured degraded outcome rather than an unclassified failure.

### FR-2: Quality Gates

LeanKernel must deterministically validate responses before accepting them as final output.

#### Requirements

- `ResponseQualityGate` must operate without another model call.
- The gate must support deterministic checks for:
  - non-empty output
  - minimum useful length
  - constraint coverage for complex or structured requests
  - refusal detection
  - optional format/schema compliance when the request requires a strict shape
- Refusal detection must distinguish between:
  - valid policy-driven refusals
  - suspect low-value refusals that should trigger escalation
- Thresholds must be configurable globally and overrideable by route or task class.
- Gate outcomes must be logged for audit with:
  - pass/fail result
  - failed checks
  - threshold values used
  - response length
  - constraint count and estimated coverage
  - whether escalation was triggered
- The gate must support outcome classes:
  - `pass`
  - `escalate`
  - `block`
  - `degrade`
- Quality checks must remain fast enough for inline execution on every turn.

#### Operational Notes

- Quality rules must be versioned so audit records can be tied to the exact rule set used.
- Gate logs must store redacted excerpts or hashes when full response storage would violate logging policy.
- Quality gates must run before response enhancement to avoid hiding a failed primary answer.

#### Acceptance Criteria

- Empty or clearly unusable responses are rejected deterministically.
- Complex requests with missing constraint coverage can trigger escalation.
- Every fail, escalation, block, or degrade decision is audit-logged with machine-readable reason codes.
- The gate can be tuned by configuration without code changes or service restart.

### FR-3: Shadow Routing

LeanKernel must support shadow routing for model evaluation and routing calibration without affecting the user-visible response.

#### Requirements

- `ShadowRoutingStrategy` must support configurable sampling rates per route, environment, or request type.
- Shadow execution must run in parallel with the primary turn when enabled.
- Shadow outputs must be logged but never returned to the user.
- Shadow execution must not create user-visible or durable side effects.
- If a shadow run requires tool context, it must use one of these modes:
  - selection-only evaluation with no model invocation
  - read-only tool simulation
  - full inference with side-effecting tools disabled
- Shadow comparisons must record at minimum:
  - primary route vs shadow route
  - latency delta
  - estimated cost delta
  - quality gate result delta
  - refusal mismatch
  - high-level disagreement summary
- Shadow budget must be isolated and capped independently from user-serving budget.
- Shadow failures must be logged and suppressed.
- Operators must be able to run observe-only mode before enabling full shadow inference.

#### Acceptance Criteria

- A shadow-enabled turn always returns the primary result, never the shadow result.
- Shadow execution cannot modify sessions, tool state, outbound channels, or engagement artifacts.
- Comparison records can be used to evaluate whether the routing policy under- or over-selected model tiers.
- Shadow failures never surface as user-facing turn failures.

### FR-4: Multi-Agent Orchestration

LeanKernel must support complex task execution through MAF-native workflows and coordinator/worker patterns.

#### Requirements

- Use `Microsoft.Agents.AI.Workflows` graph-based workflows for multi-step tasks that exceed a single-turn execution path.
- Support the agent-as-tool pattern so worker agents can be exposed to the coordinator as `AIFunction` or equivalent MAF-native callable units.
- Worker agents must be definable from Postgres and/or configuration with fields for:
  - worker ID and purpose
  - system prompt or role instructions
  - allowed tools
  - retrieval scope
  - context budget
  - expected output schema
  - concurrency/fan-out limits
  - enabled status
- Worker agents must receive scoped context only; they cannot automatically inherit the coordinator's full prompt, history, or tool set.
- Workflows must enforce:
  - max depth and max fan-out
  - cycle prevention
  - per-worker timeout budgets
  - cancellation propagation
  - idempotent retry behavior for safe nodes
- Orchestration traceability must capture:
  - workflow run ID
  - coordinator plan
  - worker invocations
  - worker outputs
  - which outputs contributed to the final answer
  - elapsed time and failure reasons per node
- Complex tasks must be able to fall back to single-agent execution when workflow orchestration is unavailable.

#### Architecture Notes

| Artifact | Purpose |
| -------- | ------- |
| `WorkflowCoordinator` | Chooses when to build a graph workflow vs single-turn execution |
| `WorkerDefinitionStore` | Loads worker definitions from Postgres/config |
| `WorkerScopeResolver` | Applies tool, retrieval, and context boundaries |
| `ContributionLedger` | Records which worker output informed the final answer |
| `WorkflowTraceStore` | Persists run and node records for replay/audit |

#### Acceptance Criteria

- Coordinators can delegate to multiple specialized workers through MAF-native workflows.
- Worker context and tool exposure are constrained by definition and enforced at runtime.
- Every workflow run is reconstructable from stored traces.
- Workflow outages or misconfiguration do not prevent a safe single-agent fallback path.

### FR-5: Response Enhancement

LeanKernel must support synchronous response enhancement before delivery while preserving reliability and auditability.

#### Requirements

- `IResponseEnhancer` must become an ordered enhancement pipeline with individually configurable steps.
- Enhancement step enablement must be configurable globally, per agent, or per route.
- The initial enhancement set must include:
  - knowledge synthesis
  - engagement file maintenance
  - refusal interception
- Knowledge synthesis must merge approved retrieved facts or workflow outputs into the final response without inventing unsupported facts.
- Engagement file maintenance must refresh the turn's engagement artifacts only within a strict time budget and no-op safely on dependency failure.
- Refusal interception must detect malformed, low-value, or policy-inconsistent refusals and either normalize them or send the turn back to escalation/degraded handling.
- Enhancement ordering must be explicit and auditable.
- Enhancement traceability must record:
  - which steps ran
  - which steps changed the response
  - timing per step
  - skipped/disabled reason
- Enhancers must not bypass quality gate, spend guard, or safety policy.

#### Default Step Order

1. refusal interception
2. knowledge synthesis
3. engagement file maintenance

#### Acceptance Criteria

- Enhancement can be enabled or disabled per step without redeploying code.
- A response record shows exactly which enhancement steps ran and whether they changed the output.
- Valid policy refusals cannot be converted into unsafe compliance by the enhancer.
- Enhancement failures degrade to the last known-good response rather than breaking delivery.

### FR-6: Post-Turn Learning Pipeline

LeanKernel must persist every completed assistant turn and learn from it asynchronously.

#### Requirements

- `PostTurnPipeline` must synchronously persist the assistant turn and publish a `TurnEvent` after a completed response.
- `TurnEvent` must include enough metadata for replay and learning, including:
  - session ID
  - user ID or actor reference
  - run ID / correlation ID
  - agent ID
  - routing decision summary
  - quality gate outcome
  - selected context summary
  - error information
  - timestamps
- `ISelfImprovementPipeline` must coordinate ordered `ILearningStep` implementations.
- The initial learning steps must include:
  - wiki fact extraction through GBrain
  - capability gap detection
  - engagement updates
- A background worker must drain turn events and execute learning steps without blocking the foreground response.
- Learning execution must be idempotent and retry-safe.
- Failed events must support retry with bounded backoff and dead-letter handling after the configured threshold.
- The system must expose backlog depth, lag, and failure metrics for the turn-event queue.
- GBrain dream-cycle integration must support autonomous enrichment over accumulated turn data during scheduled windows.

#### Operational Notes

- Foreground persistence of the assistant turn is mandatory; learning is asynchronous.
- Learning steps must be individually toggleable and versioned.
- GBrain unavailability must pause only the dependent learning steps, not the entire turn pipeline.
- Capability gaps must be persisted as structured records suitable for roadmap or benchmark review.

#### Acceptance Criteria

- Completed turns are durable even if the learning worker is offline.
- Turn events can be processed asynchronously without extending user-visible latency beyond the configured enqueue budget.
- Each learning step emits a structured result with status, timing, and failure reason.
- Dream-cycle jobs can enrich knowledge autonomously without changing the already-delivered response.

### FR-7: Scheduled Jobs and Proactive Tasks

LeanKernel must support durable scheduled and proactive execution against the same runtime and policy surface used for interactive turns.

#### Requirements

- The scheduler must support cron-based recurring jobs and one-time or boundary-driven jobs.
- Job definitions must be stored in Postgres as the source of truth.
- Job definitions must include at minimum:
  - job ID and name
  - enabled flag
  - cron expression or time-boundary trigger
  - timezone
  - target agent or workflow
  - target session/channel scope
  - prompt or task payload
  - retry policy
  - overlap policy
  - misfire policy
  - last/next execution metadata
- `TimeBoundaryService` must support user- or policy-aware time windows such as:
  - morning briefing
  - end-of-day summary
  - quiet hours
  - next active window
- Proactive jobs must execute through the agent runtime rather than bespoke one-off logic.
- Job execution must respect the same routing, spend, rate, and safety policies used for interactive turns.
- Scheduler execution must be restart-safe and duplicate-safe.
- The scheduler must support a single-active dispatcher model across replicas using a durable lease or equivalent coordination primitive.
- Job run records must capture start time, completion time, outcome, error, and correlation ID.

#### Acceptance Criteria

- Jobs persist across restarts and resume from Postgres state.
- Morning briefing and end-of-day flows can be scheduled through time-boundary rules rather than hardcoded timers.
- Proactive task runs execute through the normal agent runtime and emit normal diagnostics/traces.
- Duplicate execution is prevented under the defined lease/dispatcher model.

### FR-8: Production Hardening

LeanKernel must meet production reliability expectations for provider dependency handling, cost control, observability, and health reporting.

#### Requirements

- Provider health tracking must monitor LiteLLM route availability, failures, cooldowns, and circuit-breaker state.
- Spend guard must enforce per-session, per-day, and per-month cost limits with soft and hard thresholds.
- API endpoints must support server-side rate limiting with route-aware quotas and standard 429 behavior.
- Graceful degradation must exist for at least these dependency failures:
  - LiteLLM unavailable
  - GBrain unavailable
  - scheduler temporarily unavailable
  - workflow execution unavailable
- Full OpenTelemetry tracing must cover:
  - API ingress
  - routing and escalation
  - workflow nodes and worker calls
  - tool invocation
  - enhancement steps
  - post-turn enqueue and learning steps
  - scheduler dispatch and proactive runs
  - provider calls
- Structured logging must include correlation IDs for request, run, workflow, worker, turn event, and job execution.
- Docker health checks must exist for all required services in the compose stack, including:
  - `engine`
  - `database`
  - `litellm`
  - `qdrant`
  - `unstructured`
  - `indexer`
  - `signal`
- Health endpoints must distinguish degraded vs unavailable states.
- Production hardening controls must be observable through logs, metrics, and traces even when the user-facing response is degraded.

#### Acceptance Criteria

- Provider outages trigger fallback or degraded responses within the configured fail-fast window.
- Hard spend limits are never exceeded by accepted turns.
- Rate-limited requests return deterministic 429 responses with traceable reason codes.
- End-to-end traces cover the full turn lifecycle for the large majority of production requests.
- Docker health checks accurately reflect service readiness and dependency degradation.

## Cross-Cutting Contracts and Storage

### Configuration Precedence

Unless a stricter policy is required, Phase 3 should resolve configuration in this order:

1. code defaults
2. `appsettings.json` / environment binding under `LeanKernel:*`
3. Postgres-backed runtime overrides
4. server-side per-request policy overlays

Client input must never override provider health, spend guard, scope boundaries, or rate limits.

### Core Records

| Record | Purpose | Minimum fields |
| ------ | ------- | -------------- |
| `RoutingDecisionRecord` | Auditable route selection and escalation trail | request/run IDs, selected route, rejected routes, reason codes, health snapshot, budget snapshot |
| `QualityGateRecord` | Deterministic gate outcome | rule version, checks run, thresholds, pass/fail, escalation decision |
| `ShadowComparisonRecord` | Primary vs shadow evaluation | sample policy, primary route, shadow route, cost/latency/quality deltas |
| `WorkflowRunRecord` | Multi-agent orchestration replay | workflow ID, coordinator, node graph, start/end, status |
| `WorkerContributionRecord` | Final-answer provenance | worker ID, output hash/excerpt, selected/not selected |
| `ResponseEnhancementRecord` | Enhancement audit | step name, changed flag, duration, skip reason |
| `TurnEventRecord` | Post-turn learning payload | session, actor, route summary, quality summary, timestamps |
| `LearningStepRunRecord` | Background learning status | step, event ID, status, duration, retry count |
| `ScheduledJobRecord` | Durable proactive task definition | trigger, target, timezone, policy fields, enabled |
| `ScheduledJobRunRecord` | Scheduler execution history | job ID, due time, start/end, outcome, error |
| `ProviderHealthRecord` | Route/provider state | alias, status, failure count, cooldown expiry |
| `SpendLedgerRecord` | Cost guard evidence | scope, estimated cost, actual cost, remaining budget |

### Required Policy Controls

Phase 3 must expose server-side controls for:

- max routing attempts
- max routing latency budget
- per-route quality thresholds
- shadow sample rate and shadow budget cap
- workflow depth/fan-out limits
- per-worker tool allowlists
- enhancement step toggles and time budgets
- learning step toggles and retry counts
- scheduler overlap and misfire policies
- provider circuit-breaker thresholds
- rate-limit quotas and windows

## Non-Functional Requirements

- **NFR-1 Determinism:** Routing, gating, and escalation decisions must be reproducible from stored inputs and configuration versions.
- **NFR-2 Performance:** Added inline Phase 3 logic must preserve interactive responsiveness; routing, gating, and enhancement overhead must remain within configured p95 budgets.
- **NFR-3 Reliability:** Optional dependency failures must degrade safely rather than causing opaque turn failure.
- **NFR-4 Auditability:** Every major Phase 3 decision must emit structured logs, metrics, and trace context.
- **NFR-5 Cost Control:** Routing and shadow execution must enforce spend-aware behavior and prevent runaway experimentation costs.
- **NFR-6 Security and Privacy:** Logs, traces, and learning records must support redaction and avoid leaking secrets or unnecessary personal data.
- **NFR-7 Extensibility:** New workers, learning steps, enhancement steps, and job definitions must be addable without redesigning the turn pipeline.
- **NFR-8 Operability:** Health state, queue lag, provider failures, and job failures must be observable through standard production telemetry.

## Dependencies

- Phase 2 identity, scoped retrieval, history shaping, and channels.
- Postgres for runtime definitions, leases, and durable operational records.
- LiteLLM for routed model availability and provider metadata.
- GBrain for wiki fact extraction, capability-gap enrichment, and dream-cycle learning.
- MAF v1.6.1 runtime and `Microsoft.Agents.AI.Workflows` support.
- Existing Commander delivery path for proactive outbound execution.
- OpenTelemetry collector/export pipeline.
- Docker Compose health-check support for the full runtime stack.

## Success Metrics

- **Routing efficiency:** at least 80% of eligible turns complete on the first selected tier without escalation.
- **Escalation uplift:** at least 50% of quality-gate failures are recovered by one higher-tier retry.
- **Shadow calibration value:** shadow disagreement rate is measurable and trends downward as routing policy matures.
- **Quality audit coverage:** 100% of blocked, escalated, degraded, and refused turns have structured gate records.
- **Latency control:** Phase 3 inline overhead stays within agreed routing/gating/enhancement p95 budgets.
- **Spend adherence:** zero accepted turns exceed configured hard spend limits.
- **Learning freshness:** post-turn queue lag remains below the configured operational threshold for normal load.
- **Proactive reliability:** scheduled job success rate meets or exceeds 99% excluding dependency outages outside LeanKernel control.
- **Trace coverage:** at least 95% of production turns have end-to-end distributed traces with correlation IDs.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Routing becomes non-deterministic due to hidden runtime state | Persist routing inputs, config version, health snapshot, and budget snapshot per turn |
| Quality gates reject useful answers too often | Start with observe-only metrics, tune thresholds, and track false-positive rate |
| Shadow routing causes hidden side effects | Disable side-effecting tools and isolate shadow budget/state |
| Multi-agent workflows explode latency or token cost | Enforce max depth, fan-out, per-worker budgets, and coordinator fallback |
| Enhancement pipeline mutates safe refusals or verified content incorrectly | Make refusal interception explicit, trace all mutations, and preserve last known-good response |
| Learning backlog grows without bounds | Add bounded queue, retry policy, dead-letter handling, and operational lag alerts |
| Scheduler duplicates jobs across replicas | Use durable single-active lease/dispatcher coordination |
| Provider or GBrain outages create cascading failures | Add circuit breakers, dependency-specific degrade modes, and health-aware fallback behavior |

## Implementation Clarifications (v1 Defaults)

Use these defaults unless architecture review changes them during grooming:

- `ModelRouter` is the public Phase 3 routing façade, even if it composes or evolves the current `ModelRoutingService` implementation.
- Quality gating is deterministic and heuristic-based in v1; it does not call another model to judge another model.
- Quality gates run before response enhancement.
- Shadow routing defaults to observe-only or low-sample operation in early rollout and uses isolated budget accounting.
- Shadow runs may not execute side-effecting tools.
- Worker definitions and scheduled job definitions are runtime-managed and versioned in Postgres, with configuration used only for bootstrap defaults.
- Response enhancement is synchronous but bounded; a timed-out enhancer step becomes a no-op and the response still returns.
- Post-turn learning never blocks the final user response beyond the enqueue and persistence budget.
- GBrain dream-cycle enrichment is additive and cannot rewrite already-delivered responses.
- Scheduler execution uses a single-active dispatcher lease across replicas for v1 duplicate prevention.
- No Phase 3 UI is required; all control surfaces are API, config, runtime records, and telemetry.

## Release Acceptance Criteria

- AC-1: Routed turns produce deterministic routing diagnostics with selected route, rejected alternatives, and escalation outcome.
- AC-2: Quality gate failures produce structured audit records and trigger the configured escalation or degrade path.
- AC-3: Shadow routing can run in production-safe mode without changing the primary response or any durable side effects.
- AC-4: Complex tasks can execute through MAF workflows with reconstructable worker-contribution traces.
- AC-5: Response enhancement steps are individually toggleable, auditable, and cannot weaken valid refusal behavior.
- AC-6: `PostTurnPipeline` persists assistant turns durably and background learning continues independently of the foreground response path.
- AC-7: Scheduled jobs run through the agent runtime, survive restart, and do not duplicate under the lease model.
- AC-8: Provider outages, spend exhaustion, and rate-limit events degrade gracefully and remain fully observable.

## Sprint-Ready Engineering Tickets

- [ ] `P3-01` Define Phase 3 core contracts in `LeanKernel.Core` for routing diagnostics, quality records, shadow comparisons, workflow traces, worker definitions, scheduled jobs, and learning run records.
- [ ] `P3-02` Implement `ModelRouter`, expand `TaskComplexityScorer`, and formalize `PolicyModelSelector` plus `EscalationPolicy` with deterministic reason codes and stored routing snapshots.
- [ ] `P3-03` Extend `ResponseQualityGate` to support refusal detection, route/task thresholds, structured outcomes, and observe-only tuning mode.
- [ ] `P3-04` Upgrade `ShadowRoutingStrategy` to support sampled parallel execution, isolated budgets, read-only tool behavior, and persisted comparison records.
- [ ] `P3-05` Build MAF workflow orchestration, worker-definition loading from Postgres/config, scoped worker execution, and contribution tracing.
- [ ] `P3-06` Refactor `IResponseEnhancer` into an ordered, traceable enhancement pipeline with refusal interception, knowledge synthesis, and engagement maintenance steps.
- [ ] `P3-07` Expand `PostTurnPipeline`, `ISelfImprovementPipeline`, and `ILearningStep` execution with durable queueing, capability-gap records, GBrain-backed wiki extraction, and dream-cycle hooks.
- [ ] `P3-08` Replace static proactive registration with Postgres-backed scheduled jobs, lease-based dispatch, time-boundary triggers, and agent-runtime execution.
- [ ] `P3-09` Harden provider health, spend guard, rate limiting, graceful degradation, Docker health checks, correlation IDs, and OpenTelemetry traces end to end.
- [ ] `P3-10` Add integration and failure-injection coverage for routing determinism, quality escalation, shadow isolation, workflow traceability, learning backlog handling, scheduler duplicate prevention, and dependency degradation.
