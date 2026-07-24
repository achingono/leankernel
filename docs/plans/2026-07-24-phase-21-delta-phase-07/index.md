# Phase 21 Delta + Phase 07 — Document Enrichment, Learning & Scheduler

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Complete the Intelligent Brain enrichment pipeline for document ingestion (Phase 21 delta) and build the asynchronous learning pipeline, onboarding intelligence, cron scheduler, and Dream cycle orchestration (Phase 07). The learning pipeline runs as a **separate background service** — `LeanKernel.Services.Learning` — decoupled from the Gateway and Logic projects to enforce resource isolation and independent scaling.

## Scope

### In Scope
- **Phase 21 delta — Post-ingestion enrichment**: emit `DocumentEnrichmentRequestedEvent` after successful ingestion completion, with correlated `(IngestionJobId, EnrichmentJobId, DreamRunId)`. Add enrichment queue/job model with status lifecycle, scope-preserving derived artifacts, source-mapping contract from ingestion scope to Dream source ids, and status query surfaces.
- **Phase 07 — Learning pipeline as separate service**: a `LeanKernel.Services.Learning` background service project with:
  - Turn-event queue (bounded, non-blocking) fed from the completed turn pipeline
  - Ordered self-improvement steps: fact extraction, identity-intent extraction, capability-gap detection, engagement tracking
  - Knowledge-page update coordinator writing learned facts back under correct scope
  - Reuse of `FactExtractionService`, `MemoryPageNormalizer`, `MemoryPageLinker` from `LeanKernel.Logic`
- **Phase 07 — Onboarding intelligence**: gap detector and directive builder consuming learned identity intent
- **Phase 07 — Scheduler**: cron evaluator, time-boundary service, job executor, scheduler hosted service, scheduled-job entities/repository/migrations
- **Phase 07 — Dream cycle job**: scheduler-owned job invoking native `gbrain dream` phases with source scoping, bounded execution windows, lock-aware retry, persist phase outcomes
- **Configuration**: learning enable + step toggles + queue bounds; scheduler jobs + cron expressions; enrichment settings
- **Communication between services**: `LeanKernel.Services.Learning` communicates with `LeanKernel.Logic` via the existing event spine (`IEventCollector`/`IEventSubscriber`) and via direct service references for shared contracts (memory pages, identity, knowledge)
- **Reconciliation with existing Phase 21 exit criteria**: update evidence and exit-criteria docs for the enrichment delta

### Out of Scope
- Admin/onboarding UI (Phase 09)
- Diagnostics persistence and metrics for learning/scheduler (Phase 08), beyond emitting signals through the event spine
- Distributed/out-of-process ingestion queue (DB-backed queue is sufficient)
- Document versioning, diffing, or lifecycle management
- Phase 04 model routing, quality gates, or shadow routing

## Entry Criteria
- Phase 21 document ingestion pipeline is operational (document storage, fingerprint dedup, search/list tools)
- Event spine (`IEventCollector`, `IEventSubscriber`, `IEventStore`, `DbEventStore`) is operational with generic `Emit<T>` and `IHasEnvelope`
- Memory pipeline (`FactExtractionService`, `MemoryPageNormalizer`, `MemoryPageLinker`, `MemoryGraphReasoner`) is operational in `LeanKernel.Logic`
- Identity partitioning (`IdentityContext`, `IPermit`, `ScopeDimension`) is operational
- Turn pipeline (Phase 03) emits turn-completion events or has a hook that can feed the turn-event queue
- GBrain MCP client is operational for Dream cycle invocations
- `LeanKernel.Core.Interfaces` and `LeanKernel.Core.Entities` are available for shared contract references

## Pre-Phase: MCP Interface Relocation

`IGBrainMcpClient` and its associated DTOs (`GBrainException`, `McpToolCallParams`, `McpResult`) originally lived in `LeanKernel.Gateway.Memory`. Both `LeanKernel.Services.Learning` and the enrichment service need to call Dream cycles and memory write-backs, but `LeanKernel.Services.Learning` must not reference `LeanKernel.Services.Gateway`. Therefore, MCP contracts and implementation classes now live in `LeanKernel.Services.Common`:

1. Move `IGBrainMcpClient` + DTOs to `LeanKernel.Services.Common.Interfaces`
2. Move MCP implementations (`GBrainMcpClient`, `GBrainMemoryClient`, etc.) to `LeanKernel.Services.Common.Memory`
3. Move `GBrainSettings` to `LeanKernel.Services.Common.Configuration`
4. Move health checks (`GBrainHealthCheck`, `LiteLlmHealthCheck`) to `LeanKernel.Services.Common.HealthChecks`
5. Keep shared DI wiring in `LeanKernel.Services.Common.Extensions`
6. Add `IDreamService` abstraction in `LeanKernel.Services.Common.Interfaces` (with Gateway implementation)

## Design Decisions

### Learning as Separate Service (`LeanKernel.Services.Learning`)
- New console/background-service project with its own `appsettings.json`, DI container, and hosted service loop
- References `LeanKernel.Core` for shared contracts (entities, interfaces, event contracts), `LeanKernel.Logic` for memory/fact-extraction services, and `LeanKernel.Services.Common` for GBrain MCP services
- Does NOT reference `LeanKernel.Services.Gateway` — accesses MCP/gbrain functionality through `IDreamService` and `IGBrainMcpClient` via `LeanKernel.Services.Common`
- Receives turn-completion events via `IEventStore` (DB-backed persistence as queue of record), with in-memory channel for low-latency delivery
- On startup, recovers missed events from `IEventStore` to handle restarts
- Runs the self-improvement pipeline in a background loop — no HTTP endpoint, no gateway dependency
- The scheduler hosted service lives here as well, alongside the Dream cycle job

### Enrichment Pipeline
- `DocumentEnrichmentRequestedEvent` emitted via `IEventStore.AppendAsync` (NOT `IEventCollector`, which is request-scoped) after successful `IngestDocumentAsync`
- Enrichment uses `IDbContextFactory<EntityContext>` for DB access (singleton-safe, same pattern as `DocumentIngestionQueue`)
- Direct enqueue path: `DocumentIngestionHostedService` enqueues enrichment jobs directly to `IEnrichmentQueue`
- Enrichment worker runs in `LeanKernel.Logic` (co-located with ingestion worker) to avoid project dependency churn
- Scope-preserving: derived artifacts inherit the original `AvailabilityScope` and cannot broaden visibility

## Roles
- Owner: (agent)
- Reviewer: Separate agent session
- Approver: Repository owner
