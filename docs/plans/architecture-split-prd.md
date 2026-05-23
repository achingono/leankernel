# PRD: Architecture Split Decision — Modular Monolith vs Microservices

**Status:** Under Review  
**Audience:** Engineering lead, maintainer, future contributors  
**Reviewed by:** Second-model architectural review (GPT-5.2) — critique incorporated below  
**Last updated:** 2025

---

## Overview

This document evaluates whether to split LeanKernel's modular monolith into independent microservices, when to do it, and which parts justify extraction. It also serves as the authoritative migration guide if the split decision is triggered.

---

## Problem Statement

LeanKernel runs as a single ASP.NET Core process composing Commander, Thinker, Archivist, Scheduler, and Plugins. Stakeholders are concerned that:

1. As features grow, upgrade-related coupling could reproduce the fragmentation seen in OpenClaw and Hermes agent ecosystems—where cross-module contract drift, incompatible release cadences, and coordinated schema migrations became dominant maintenance costs.
2. Some workloads (doc indexing, skill execution, inbound channels) have resource and availability profiles that differ from the core reasoning path, and running them in the same process creates resource contention and blast radius.
3. The absence of a clear decision framework means any engineer could justify extraction on gut instinct, causing inconsistent architecture drift.

---

## Goals

- Produce a pragmatic split decision grounded in actual LeanKernel scale and constraints.
- Define measurable trigger conditions that decide when to revisit.
- Provide a phased migration plan that is safe at every gate.
- Document risks, rollback strategies, and communication model invariants.

## Non-Goals

- Multi-tenant deployment topology.
- Cloud-native Kubernetes orchestration (unless trigger conditions are met).
- Replacing litellm, qdrant, unstructured, or signal sidecars (already extracted, already working).

---

## Challenged Assumptions

The following assumptions appeared in earlier planning discussions and are explicitly challenged here.

| Assumption | Challenge |
|------------|-----------|
| "Microservices = operational maturity" | For a self-hosted single-tenant personal AI agent with one active user, microservices replace in-process function calls with network hops, distributed tracing requirements, and multi-container debugging. This is operational complexity without benefit at this scale. |
| "Seams now, extraction later has low cost" | Every added abstraction layer carries cognitive overhead, test surface, and serialization points. If extraction criteria are not defined before adding seams, the seams become a permanent second architecture rather than a migration tool. |
| "512 MB engine limit is fine after adding OTel + audit + sandboxing" | OpenTelemetry batching, audit log buffers, and sandboxed skill runners each add allocations and background threads. Memory budget must be explicitly re-verified at each phase. |
| "Adding PostgreSQL is inherently better than JSON/SQLite" | PG adds a daemon, backup discipline, and migration tooling. JSON/SQLite at single-tenant personal-agent scale is reliable and debuggable. PG is only justified when concurrent write access, complex queries, or multi-process access are required—none of which apply today. |
| "Broker seam = RabbitMQ/NATS provider swap" | Message delivery semantics (ordering, idempotency, retry, backpressure, poison handling) are far more complex than provider selection. The SQLite-backed persistent queue already covers durability at single-instance scale. The seam is valuable; the broker should not be added until concurrent producers or consumers are required. |
| "Reasoning→Memory can be synchronous HTTP/gRPC" | The current in-process call from Thinker→Archivist is sub-millisecond. Moving it cross-process introduces latency, timeout failures, partial-context degradation, and serialization overhead for the hottest path in the system. This extraction has the worst benefit-to-cost ratio of any candidate. |
| "Skill isolation requires microservices" | Subprocess execution with resource limits, seccomp profiles, and timeout enforcement achieves capability isolation without a network boundary. WASM sandboxing is a viable alternative. Neither requires splitting the engine. |
| "Channel Gateway is a weak extraction candidate" | The reverse is closer to true: channels impose rate limits, connection state, and availability contracts that differ from the reasoning path. Isolating channel adapters protects reasoning latency from slow external APIs and prevents a misbehaving channel from crashing the core runtime. This is the strongest extraction candidate after the already-extracted sidecars. |
| "Phase 7 service extraction is one step" | Extraction of any in-process component requires: interface stabilization, dual-run period, compatibility shim, rollback plan, contract test coverage, and operational rehearsal. Treating it as a single phase underestimates the engineering effort by at least 3×. |
| "Self-hosted = simpler operations" | Self-hosted is operationally variable: hardware heterogeneity, no managed backup, manual TLS, and no on-call SRE. Each new container adds operational surface. Fewer containers is strictly better until a concrete isolation requirement forces a boundary. |

---

## Target Services

The table below describes the desired end state if a full split is ever warranted. Column "Extract?" encodes the recommendation for self-hosted single-tenant today.

| Service | Responsibility | Current source | State | Extract now? | Justification |
|---------|---------------|----------------|-------|-------------|---------------|
| **API/Web Host** | Blazor UI, REST/OpenAI-compat API, auth, admin, health | LeanKernel.Host | Stateless (cookies only) | No — keep as engine | No independent resource profile; extraction adds cold-start latency to UI |
| **Channel Gateway** | Inbound/outbound channel adapters (Signal, webhook, Discord), time-gated delivery, outbound queue | LeanKernel.Commander | Queue offsets + delivery state | **Conditional** (see triggers) | Protects reasoning from channel rate-limit pressure; strong candidate when second channel is added |
| **Reasoning Worker** | Turn orchestration, model routing, prompt assembly, tool call planning, self-improvement pub | LeanKernel.Thinker | Session refs (via Archivist), model telemetry | No — highest extraction cost | Tightest in-process coupling to Archivist; sync cross-process call is a reliability regression |
| **Memory Service** | Sessions, wiki, context gating, embedding cache, Qdrant retrieval | LeanKernel.Archivist | JSON files (sessions/wiki), Qdrant | No — coupled to Reasoning | Extraction without Reasoning extraction creates a distributed synchronous call on the hot path |
| **Indexing Worker** | Doc parse, chunk, embed, tag, vector write | Python indexer sidecar | SQLite state + Qdrant | **Already extracted** | Correct — long-running batch work with its own dependency chain (Unstructured + LiteLLM) |
| **Scheduler Worker** | Cron jobs, proactive tasks, wiki maintenance, model limit sync | LeanKernel.Scheduler | Cron state | Deferred | Low-frequency; no resource contention today; extract when it needs independent deploy cadence |
| **Skill Runner** | Sandboxed tool/skill execution with resource limits | LeanKernel.Plugins | Ephemeral | **Strong candidate for isolation** | Skill execution has an independent blast-radius requirement; subprocess/WASM isolation inside the engine is the preferred path before a full service split |
| **Observability Stack** | OTel collector, metrics backend, dashboards | None today | External | Yes — as OTel sidecar | Low operational cost; high diagnostic value; no code coupling |
| **litellm / qdrant / unstructured / signal** | Model proxy, vectors, doc parser, Signal daemon | Python/external sidecars | Already extracted | **Already extracted** | Correct; do not change |

---

## Communication and Consistency Model

### Current model (in-process)

All Thinker→Archivist and Thinker→Plugins calls are synchronous in-process DI calls. No serialization, no network latency, no timeout configuration required.

### Target invariants for any future cross-process boundary

Before any module crosses a process boundary, these invariants must be documented and tested:

| Invariant | Definition |
|-----------|------------|
| **Delivery semantics** | At-least-once with idempotency key on every message; deduplicate at consumer |
| **Message versioning** | All messages carry a schema version field; consumers must tolerate unknown future fields (forward compatibility) |
| **Contract evolution** | Breaking changes to cross-process contracts require a compatibility shim and a deprecation window of ≥ 2 releases |
| **Timeout budget** | Every synchronous cross-process call must declare a timeout; the caller must define degraded behavior (partial context, skip-and-continue, or fail-open) |
| **Idempotency** | All state-mutating operations across a network boundary must be safe to replay; use idempotency keys and last-write-wins or optimistic concurrency |
| **Circuit breaker** | Synchronous calls to extracted services must use a circuit breaker with defined open/half-open/closed thresholds |

### OpenClaw/Hermes anti-patterns to avoid

| Anti-pattern | Prevention |
|--------------|-----------|
| Module A v2 incompatible with Module B v1 at runtime | All cross-process contracts use additive schema evolution; never remove fields, only deprecate |
| Multi-service coordinated deployments for feature releases | Only extract when deployment cadences are genuinely independent; if two services must deploy together, they are not independently deployable |
| Cross-service schema migration requiring downtime | Apply the Expand/Contract migration pattern: add new columns/fields first, backfill, then remove old ones in a later release |
| Cross-service debugging requires correlated log search | Add OTel trace correlation *before* any service is extracted; never extract a service without end-to-end tracing in place |
| "Just one more async event" creating hidden ordering dependencies | Model the event DAG explicitly before adding broker-mediated events; validate ordering under failure with chaos tests |

---

## Phased Migration Plan

Phases are ordered by risk-reduction value, not extraction ambition. Observability precedes any distributed work. Extraction is deferred to a trigger-gated phase.

### Phase 0 — Foundation (Done)

Rename, project structure, basic CI, license. ✅

### Phase 1 — Quality Gates (Done)

Coverage threshold, SonarQube scan scripts, repeatable test commands. ✅

**Gate:** `scripts/quality/test-coverage.sh` and `scripts/quality/sonarqube-scan.sh` pass cleanly.

---

### Phase 2 — Observability First

**Rationale:** Observability is installed *before* state abstraction and broker work so that every subsequent change has a diagnostic baseline. This is the primary lesson from the OpenClaw fragmentation—services were split before anyone could measure them.

**Scope:**
- Add OpenTelemetry SDK to LeanKernel.Host; propagate trace context through Commander → Thinker → Archivist call chain.
- Add correlation ID to every `LeanKernelMessage` and log context.
- Emit metrics: turn latency p50/p95, model call latency, queue depth, tool call count/latency, context token count.
- Add Docker Compose service: `jaeger-all-in-one`. Export traces directly from the .NET SDK to Jaeger to save the memory overhead of an intermediate OTel Collector.
- Add `/api/metrics` Prometheus scrape endpoint directly to the .NET app.
- Add per-component health probes (Qdrant reachability, LiteLLM reachability, queue saturation).

**Memory budget check:** Baseline engine RSS must be re-measured after OTel SDK integration. If RSS exceeds 400 MB under normal load, increase the engine memory limit before continuing.

**Exit gate:**
- Traces for a complete turn (inbound → model call → response → session write) are visible end-to-end in the local Jaeger UI.
- `turn_latency_p95` metric is emitted and visible in Prometheus.
- All health probes return structured JSON.
- No regression in existing test suite.

---

### Phase 3 — State Abstraction (interfaces, not migration)

**Rationale:** Add repository interfaces for sessions, wiki, runtime config, and the outbound queue. Keep all existing JSON/SQLite implementations working and passing. This creates the seam for future storage migration without imposing a PostgreSQL dependency today.

**Scope:**
- Define `ISessionStore`, `IWikiStore`, `IRuntimeConfigStore`, `IOutboundQueueStore` in `LeanKernel.Core.Interfaces`.
- Refactor Archivist and Commander to use these interfaces (current file/SQLite implementations become the "local" adapters). Explicitly mandate that SQLite providers must be configured with **WAL (Write-Ahead Logging) mode** and `SYNCHRONOUS=NORMAL` to handle concurrent reads/writes efficiently.
- Do **not** add PostgreSQL providers in this phase. The adapter interfaces are the deliverable.
- Add unit tests that verify the adapter contracts.

**Why PG is deferred:** PG adds a second daemon with its own backup discipline, migration tooling, and failure mode (postgres OOM, pg_isready latency). At single-tenant personal-agent scale, this is operational complexity without benefit. PG becomes justified when: (a) concurrent write access is required, (b) cross-process read queries are needed, or (c) point-in-time recovery SLA is established.

**Exit gate:**
- All existing tests pass unchanged.
- New interface contracts have unit test suites with mock implementations.
- JSON/SQLite adapters are the registered default; no new services added to Docker Compose.

---

### Phase 4 — Outbound Queue Abstraction and Resilience

**Rationale:** The SQLite-backed persistent queue is reliable at single-instance scale. This phase hardens it with idempotency, retry policy, and dead-letter support—without introducing a broker.

**Scope:**
- Add idempotency key to every `QueuedMessage`.
- Add explicit retry policy (max attempts, backoff, jitter) and dead-letter destination to the `IOutboundQueueStore` contract.
- Implement dead-letter log (JSON file or SQLite table) with an admin UI page.
- Add chaos test: simulate delivery failure and verify retry/dead-letter behavior.
- Leave a documented broker adapter slot in `IOutboundQueueStore` for future RabbitMQ/NATS without implementing it.

**Exit gate:**
- Dead-letter path is covered by integration tests.
- Admin UI shows queue depth and dead-letter count.
- At-least-once + idempotency contract is documented in interface XML docs.

---

### Phase 5 — Security and Audit

**Rationale:** Security is a design input, not a late add-on. Before any service is extracted, access controls, scoped tokens, and audit events must be in place.

**Scope:**
- Scoped API tokens with explicit permission claims (e.g., `lk:chat`, `lk:admin`, `lk:skill:execute`).
- Audit log: append-only structured records for all state-changing operations (session mutation, config change, tool execution, admin action). Write to a tamper-evident append file and expose via admin API.
- Configuration versioning: every write to `runtime-settings.json` creates a numbered snapshot with actor and timestamp.
- Threat model document: define trust boundaries, secret management (env vars + secret mounts, never commit), encryption at rest (volumes), TLS in Docker Compose.
- Admin-only endpoints must require the `lk:admin` scope.

**Exit gate:**
- Every state-changing API endpoint emits an audit record.
- Audit records are covered by integration tests under partial failure (queue flush on shutdown, no silent drops).
- Threat model document exists in `docs/architecture/threat-model.md`.
- Scope enforcement tested by unauthorized-caller tests.

---

### Phase 6 — Skill Isolation (in-process sandbox)

**Rationale:** Skills/tools run in the engine process today. Arbitrary SKILL.md tools represent an untrusted code surface. Isolation is achievable without a microservice.

**Scope:**
- WebAssembly (WASM) based runner: evaluate and implement a WASM runtime (e.g., Extism or Wasmtime) to execute untrusted SKILL.md tools. This avoids the brittle nature of OS-level `cgroups` or user-forking across different host environments.
- WASM provides a pristine, cross-platform sandbox with strict memory limits, CPU time constraints, and zero default network/file-system access out-of-the-box.
- Timeout enforcement and hard kill path within the WASM host.
- Stdout/stderr capture with size limits and redaction of secrets.
- FS boundary: WASM runtime exposes only a scoped scratch directory via WASI; no access to wiki, sessions, or config.
- Network egress policy: WASM guest has no network access unless the skill manifest explicitly declares `network: true` and the host grants the WASI capability.
- Preserve in-process path for built-in tools (these are trusted, compiled code).
- Resource accounting: emit `skill_execution_duration_seconds` and `skill_execution_memory_bytes` metrics.

**Skill Runner as a separate service is explicitly deferred until:** skill execution volume requires horizontal scaling, or multiple independent skill trust boundaries are required (e.g., untrusted user-uploaded skills vs. curated library skills).

**Exit gate:**
- WASM isolation smoke-tested with a malicious SKILL.md that attempts to read wiki files (must be blocked by WASI capability restrictions).
- Resource limit timeout test: skill that runs indefinitely is killed within configured host timeout.
- Existing built-in tools unchanged and passing.

---

### Phase 7 — Trigger-Gated: Channel Gateway Extraction

**This phase is conditional. Execute only when trigger conditions are met (see Decision Gates).**

**Rationale for extraction:** Channel adapters (Signal, webhook, future Discord/Slack) impose external rate limits and connection lifecycles that differ from the reasoning path. A slow or crashing channel adapter can block the SQLite queue processor and delay reasoning-path responses. Extraction isolates this blast radius.

**Scope:**
- Extract `LeanKernel.Commander` (channel adapters + outbound queue) into a standalone `leankernel-channel` Docker service.
- Communication boundary: `leankernel-channel` → `leankernel-engine` via HTTP POST to `/api/inbound` (existing endpoint, already exists). `leankernel-engine` → `leankernel-channel` via broker message (or polling against the outbound queue API).
- Deploy as a separate container in Docker Compose with its own memory limit and restart policy.
- Dual-run period: run both in-process and extracted channel for ≥ 1 week with traffic comparison.
- Compatibility shim: the `IOutboundQueueStore` abstraction (Phase 3) allows the channel service to use the same SQLite store via file mount initially, then migrate to broker-mediated delivery.

**What is NOT extracted:** Thinker, Archivist, Scheduler, Plugins remain in the engine. The Reasoning→Memory coupling is too tight for synchronous cross-process extraction without a measurable user-latency regression.

**Rollback plan:**
1. Re-enable the in-process Commander registration in DI (never deleted — kept as feature flag `LeanKernel:Commander:Mode: [InProcess|Extracted]`).
2. Set `LEANKERNEL_COMMANDER_MODE=InProcess` in environment.
3. Scale down the `leankernel-channel` container.
4. No data migration required (queue remains SQLite, shared via volume mount).

**Exit gate:**
- Contract tests for the channel → engine HTTP boundary passing in CI.
- Dual-run comparison shows ≤ 5% turn latency regression (measured via OTel p95).
- Rollback tested in staging by toggling the feature flag.
- Dead-letter queue behavior identical in extracted and in-process modes.

---

### Phase 8 — Trigger-Gated: Full Reasoning/Memory Extraction

**Execute only if multi-user or multi-instance trigger conditions are met.**

This phase is explicitly out of scope for single-tenant self-hosted deployment. If LeanKernel grows to require multiple concurrent reasoning workers (e.g., multi-user, high-volume agents), the Reasoning/Memory split may be justified. At that point, this section should be expanded into its own dedicated PRD with:
- Chosen IPC mechanism (gRPC preferred over REST for latency-sensitive reasoning path)
- Context serialization format and versioning
- Session lock/concurrency strategy (optimistic locking, session affinity, or distributed lock)
- Full consumer-driven contract test suite

---

## Decision Gates — When to Revisit

| Trigger | Action |
|---------|--------|
| Engine memory RSS exceeds 400 MB under normal single-user load | Investigate allocation hotspots first; if channel adapters are the source, accelerate Phase 7 |
| A second channel adapter (Discord, Slack) is added | Re-evaluate Phase 7: channel extraction becomes the correct boundary when managing multiple external connection lifecycles |
| Skills volume exceeds 10 concurrent executions | Consider Skill Runner as separate container |
| More than one active user requires simultaneous reasoning | Begin full Reasoning/Memory extraction PRD |
| Any module upgrade requires a coordinated deploy of two or more other modules | This is an OpenClaw/Hermes signal — immediately audit contract coupling and address at the interface level before extracting |
| Single turn p95 latency exceeds 8 seconds and profiling implicates in-process resource contention | Re-evaluate extraction boundary for the contending module |
| Docker Compose host machine drops below 2 GB free RAM | Add resource limits to all containers; do NOT add new containers until headroom is restored |

---

## Cost-Benefit Matrix

| Dimension | Modular Monolith (current) | Phase 7: Channel extracted | Full microservices |
|-----------|--------------------------|---------------------------|-------------------|
| **Turn latency (Thinker→Archivist)** | ~0 ms (in-process) | ~0 ms (unchanged) | +10–50 ms (cross-process + serialization) |
| **Debug-ability** | High: single log, single process, breakpoints | High for engine; medium for channel | Low: requires distributed tracing and multi-service log correlation |
| **Upgrade coordination** | Compile-time — DI won't build if contracts are broken | Engine and channel must coordinate queue schema | Every service pair must negotiate contracts independently |
| **Memory efficiency** | Shared heap | Two heaps; channel ~64–128 MB | 6–8 heap boundaries; ~1.5–2 GB total minimum |
| **Deploy complexity** | `docker compose up` | `docker compose up` + channel service | Orchestration tooling required (or very complex Compose) |
| **Schema migration** | Single context, one `dotnet ef migrate` | Two databases or shared volume | Coordinated migrations, Expand/Contract required across 5+ services |
| **Self-hosted viability** | High | High | Low: requires operator expertise |
| **OpenClaw/Hermes risk** | Low (single compile unit) | Low-Medium (two services, stable HTTP contract) | High: independent versioning creates contract drift |
| **Failure blast radius** | Channel error can affect reasoning queue | Channel failure is isolated | Any service failure can cascade |
| **Observability requirement** | Structured logs sufficient | OTel traces for channel boundary | Full distributed tracing is mandatory, not optional |
| **Operational toil (per upgrade)** | Low | Low | High: multi-image build, push, pull, rolling restart |

**Quantified estimates for single-host self-hosted deployment:**

| Metric | Modular Monolith | Phase 7 | Full split |
|--------|-----------------|---------|-----------|
| Min RAM (containers) | ~1.5 GB total stack | ~1.6 GB | ~2.5–3 GB |
| Containers count | 7 (current) | 8 | 12–14 |
| New network hops per turn | 0 | 0 | 3–5 |
| Upgrade test surface | 1 binary | 2 binaries | 6–8 binaries |
| Mean time to debug a failing turn | ~5 min (logs) | ~8 min (OTel) | ~20–30 min (distributed trace + log join) |

---

## Final Recommendation

**Retain the modular monolith. Invest in seams, observability, and isolation — not extraction.**

The existing architecture is correct for single-tenant self-hosted deployment. The risk of over-engineering is higher than the risk of under-engineering at this scale.

| Priority | Action |
|----------|--------|
| **Do now** | Phase 2 (Observability) — establish the diagnostic baseline before any other change |
| **Do next** | Phase 3 (State Abstraction) + Phase 5 (Security) in parallel sprint tracks |
| **Do soon** | Phase 4 (Queue Resilience) + Phase 6 (Skill Isolation) |
| **Defer until trigger** | Phase 7 (Channel Gateway Extraction) |
| **Do not do without dedicated PRD** | Full Reasoning/Memory/Scheduler extraction |

### Why not split now

1. **The OpenClaw/Hermes lesson is a warning against premature splitting**, not a reason to pre-emptively fragment. Both ecosystems suffered from splitting before contracts were stable and before observability existed. LeanKernel's modular monolith with strong interfaces is *the correct preventive measure* — not microservices.
2. **There is no observed resource contention, latency problem, or deployment cadence mismatch** that would justify extraction today. Adding operational complexity before there is a problem is speculative architecture.
3. **The existing sidecar extractions** (indexer, litellm, qdrant, unstructured, signal) were each correct because they had genuine independent runtime profiles. That same logic does not apply to Thinker, Archivist, or Scheduler today.
4. **Distributed debugging is expensive.** At single-user personal-agent scale, the developer and the operator are the same person. A multi-service trace is several times harder to debug than a structured log from a monolith.

### What makes the monolith fragile today (and how to fix it without splitting)

| Risk | Fix (in-process) |
|------|-----------------|
| Channel adapter crash affects reasoning queue | Phase 6: WASM isolation for skill runner; Phase 7 (conditional): channel gateway extraction |
| No observability for cross-module performance regression | Phase 2 (OTel) |
| State stores not testable in isolation | Phase 3 (repository interfaces) |
| Queue failure modes untested | Phase 4 (idempotency + dead-letter) |
| No audit trail for admin ops | Phase 5 (audit log) |
| Untrusted skill code runs in engine process | Phase 6 (WASM isolation) |

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Seam interfaces become a permanent second architecture if extraction criteria are never met | Medium | High | This PRD defines explicit trigger conditions; review triggers quarterly |
| Memory pressure from OTel + security + sandboxing exceeds 512 MB limit | Medium | Medium | Measure RSS after each phase gate; increase limit proactively; profile allocations |
| SQLite queue loses messages on hard crash of the engine container | Low | High | Phase 4 ensures WAL mode, forced checkpoint, and dead-letter; test recovery in CI |
| Contract drift between channel-extracted (Phase 7) and engine if maintained independently | Medium | High | Consumer-driven contract tests before Phase 7; never silently change the inbound HTTP schema |
| Skill WASM isolation is bypassed by a crafted SKILL.md | Low | Medium | Restrict WASI capabilities (FS/network); red-team test in Phase 6 exit gate |
| Observability data volume fills disk on self-hosted host | Low | Medium | Rolling retention on OTel collector; log volume limits; document pruning runbook |
| Phase 7 dual-run period introduces duplicate message delivery | Medium | High | Idempotency key (Phase 4) is a prerequisite; verify dedup in contract tests |

---

## Migration Strategy for Phase 7 (Channel Gateway)

If trigger conditions are met:

1. **Stabilize the inbound HTTP contract:** `/api/inbound` is already an HTTP endpoint. Add an explicit contract test suite (e.g., Pact) that validates request/response schema.
2. **Add feature flag:** `LeanKernel:Commander:Mode: InProcess | Extracted`. Default: `InProcess`.
3. **Build `leankernel-channel` service:** Move `LeanKernel.Commander` registration to a new `leankernel-channel` Docker service entry in Compose. Service POSTs normalized `LeanKernelMessage` to engine's `/api/inbound`.
4. **Dual-run period (≥ 1 week):** Run both modes; compare turn completion rates and latency via OTel dashboards.
5. **Promote:** Set default flag to `Extracted` after dual-run passes.
6. **Rollback:** Toggle `LEANKERNEL_COMMANDER_MODE=InProcess`; scale down `leankernel-channel`. No data migration needed (shared SQLite volume).

---

## Rollback Strategy (All Phases)

| Phase | Rollback method | Data impact |
|-------|----------------|-------------|
| 2 (OTel) | Remove OTel SDK packages and sidecar from Compose | None — OTel is read-only instrumentation |
| 3 (State abstraction) | Revert interface wiring; delete unused interfaces | None — JSON/SQLite adapters unchanged |
| 4 (Queue resilience) | Revert idempotency key addition (backward compatible if consumers ignore new field) | None — SQLite schema additions are backward compatible |
| 5 (Security) | Revert token scope enforcement (can be feature-flagged) | Audit log files remain; they are append-only |
| 6 (Skill isolation) | Toggle WASM runner back to in-process | None |
| 7 (Channel extraction) | Set `LEANKERNEL_COMMANDER_MODE=InProcess` | None — SQLite queue is shared via volume |

---

## Data Lifecycle and Reliability

This section addresses concerns raised in the architectural review.

### Backup and restore

- Wiki (Markdown), sessions (JSON), and runtime config (JSON) must be covered by a documented backup runbook in `docs/development/`.
- Backup target: host-level `./data` directory snapshot + Qdrant collection export.
- Restore must be tested on a periodic schedule (quarterly minimum).
- Qdrant can be fully rebuilt from the indexer given source documents; it is a derived store.

### Retention and compaction

- Session files: keep all by default; add optional retention policy under `LeanKernel:Sessions:RetentionDays`.
- Dead-letter queue: review weekly; purge entries older than 30 days.
- Logs: existing 14-day rolling policy is sufficient.
- Self-improvement queue: drained by worker; orphaned events should be purged after 7 days.

### Concurrency control

- Sessions are single-writer per channel/sender; no cross-session locking is required today.
- Turn ordering is enforced by the in-process async queue; duplicate message detection uses the idempotency key added in Phase 4.
- If channel extraction (Phase 7) introduces concurrent inbound POST calls for the same session, session-level locking must be implemented before Phase 7 is promoted.

### Degradation modes

| Dependency failure | Expected behavior |
|-------------------|--------------------|
| Qdrant unavailable | Archivist falls back to wiki-only context; log warning; no hard failure |
| LiteLLM unavailable | Turn fails with a structured error response; no session corruption |
| SQLite queue locked | Outbound queue halts delivery; inbound turns still succeed (no coupling) |
| OTel collector unavailable | Traces dropped silently (batched export with no-op fallback); main path unaffected |
| Unstructured unavailable | Attachment processing fails gracefully; message still processed without attachment text |

---

## Acceptance Criteria

- AC-1: Every phase gate is a passing CI run with no new quality regressions.
- AC-2: Trigger conditions are reviewed at the start of every major feature planning cycle.
- AC-3: No module extraction begins without OTel instrumentation covering that module's primary call path.
- AC-4: Channel Gateway extraction (Phase 7) requires dual-run evidence before promotion.
- AC-5: Every cross-process contract has a consumer-driven contract test before the module is extracted.
- AC-6: Rollback for any phase can be executed in under 10 minutes via environment variable change or config toggle.

---

## Sprint-Ready Engineering Tickets

### Phase 2 — Observability

- [ ] `OBS-01` Add OpenTelemetry SDK packages to LeanKernel.Host; wire `ActivitySource` and `MeterProvider` startup.
- [ ] `OBS-02` Add correlation ID middleware; propagate `TraceId` through `LeanKernelMessage`, Thinker turn, and Archivist context calls.
- [ ] `OBS-03` Instrument Commander queue enqueue/dequeue with spans and queue depth gauge.
- [ ] `OBS-04` Instrument Thinker: turn latency histogram, model call span, tool call span, context token count gauge.
- [ ] `OBS-05` Instrument Archivist: wiki query span, Qdrant search span, context gate selection duration.
- [ ] `OBS-06` Add `jaeger-all-in-one` service to Docker Compose (direct trace export); add `/api/metrics` endpoint for direct Prometheus scraping.
- [ ] `OBS-07` Add structured health probes for Qdrant, LiteLLM, queue saturation; expose via `/api/health` detailed response.
- [ ] `OBS-08` Validate full turn trace is visible end-to-end in Jaeger; add regression test asserting `TraceId` is present in response headers.

### Phase 3 — State Abstraction

- [ ] `STATE-01` Define `ISessionStore`, `IWikiStore`, `IRuntimeConfigStore` in `LeanKernel.Core.Interfaces`; document contract semantics.
- [ ] `STATE-02` Refactor Archivist to use `ISessionStore` and `IWikiStore`; keep existing file-backed implementations as `LocalSessionStore` and `LocalWikiStore`.
- [ ] `STATE-03` Refactor Commander to use `IOutboundQueueStore` backed by existing SQLite implementation.
- [ ] `STATE-04` Add unit test suites for each interface contract using mock implementations.
- [ ] `STATE-05` Document PG adapter extension points in `ISessionStore` with commentary; do not implement.

### Phase 4 — Queue Resilience

- [ ] `QUEUE-01` Add `IdempotencyKey` and `RetryPolicy` to `QueuedMessage`; migrate existing SQLite schema.
- [ ] `QUEUE-02` Implement dead-letter destination (SQLite table `dead_letter_queue`); add admin API and UI page.
- [ ] `QUEUE-03` Add chaos integration test: simulate delivery failure, verify retry backoff, verify dead-letter after max attempts.
- [ ] `QUEUE-04` Document broker adapter slot in `IOutboundQueueStore` with commented-out interface extension point.

### Phase 5 — Security and Audit

- [ ] `SEC-01` Add scoped API token model with permission claims (`lk:chat`, `lk:admin`, `lk:skill:execute`); enforce in authorization filters.
- [ ] `SEC-02` Implement append-only `AuditLog` writer; integrate into all state-mutating controllers and admin services.
- [ ] `SEC-03` Add configuration versioning: snapshot `runtime-settings.json` on every write with actor and timestamp.
- [ ] `SEC-04` Write `docs/architecture/threat-model.md` covering trust boundaries, secret management, TLS policy, and encryption at rest.
- [ ] `SEC-05` Add unauthorized-caller integration tests verifying scope enforcement rejects requests lacking required claims.

### Phase 6 — Skill Isolation

- [ ] `SKILL-01` Implement `WasmSkillRunner` (via Extism/Wasmtime) that provides a pristine sandbox, enforces FS boundary via WASI, captures stdout/stderr with size limit, and hard-kills on timeout.
- [ ] `SKILL-02` Add `network: bool` and `filesystem_access: bool` to SKILL.md manifest; enforce defaults (both false).
- [ ] `SKILL-03` Emit `skill_execution_duration_seconds` and `skill_execution_memory_bytes` metrics.
- [ ] `SKILL-04` Add security smoke test: SKILL.md that attempts to read a wiki file is blocked; SKILL.md that runs indefinitely is killed within timeout.
- [ ] `SKILL-05` Preserve in-process execution path for compiled built-in tools; apply WASM isolation path only to SKILL.md runtime skills.

---

## Dependencies

- OTel SDK (.NET): `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- Phase 4 depends on Phase 3 (IOutboundQueueStore abstraction must exist before idempotency key changes)
- Phase 7 depends on Phase 2 (OTel must be in place for dual-run comparison), Phase 4 (idempotency keys required for channel extraction safety), and Phase 5 (audit trail required for cross-service auth)

---

## Open Questions

1. **Broker Selection (Answered):** RabbitMQ is explicitly ruled out due to operational complexity and memory footprint (Erlang VM). If a broker becomes necessary, use a lightweight binary like **NATS** (or NATS Embedded). Otherwise, continue to use SQLite + polling for self-hosted instances.
2. **OTel Backend (Answered):** A self-hosted personal agent should not rely on SaaS (Grafana Cloud) for basic telemetry, as it breaks offline capability and introduces privacy concerns. Stick to a lightweight local setup (e.g., direct export to Jaeger All-in-One for traces and direct Prometheus scraping for metrics).
3. If a second active user is anticipated within 12 months, Phase 8 planning should begin in parallel with Phase 6—should this be tracked?
4. What is the accepted RTO (recovery time objective) for a full `docker compose down/up` cycle? This determines whether Phase 4 dead-letter handling needs a notification hook.
