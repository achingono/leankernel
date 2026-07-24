# Phase 21 Delta + Phase 07 Exit Criteria

## Gate Checklist

### Pre-Phase: MCP Refactoring
- [ ] `IGBrainMcpClient` + DTOs moved to `LeanKernel.Services.Common.Interfaces`
- [ ] `IDreamService` abstraction added to `LeanKernel.Logic` with `GBrainDreamService` implementation in Gateway
- [ ] Shared DI helpers (`AddGBrainServices`, `AddFactExtractionChatClient`) extracted to `LeanKernel.Logic`
- [ ] Gateway still functions correctly after MCP relocation

### Phase 21 Delta — Enrichment
- [ ] `DocumentEnrichmentRequestedEvent` is emitted via `IEventStore.AppendAsync` (NOT `IEventCollector`) after successful ingestion job completion
- [ ] `IEnrichmentQueue` enqueues, claims, completes, and fails enrichment jobs with proper lease semantics
- [ ] `EnrichmentHostedService` processes enrichment jobs and invokes fact extraction on extracted text
- [ ] `EnrichmentHostedService` calls `RecoverStaleLeasesAsync` on startup
- [ ] `FactExtractionService` keyed `IChatClient("fact-extraction")` is available in `LeanKernel.Logic` DI for enrichment service
- [ ] Derived artifacts from enrichment inherit the original `AvailabilityScope` — no scope broadening
- [ ] Correlation chain `(IngestionJobId → EnrichmentJobId → DreamRunId)` is durable and queryable
- [ ] Source-mapping contract from ingestion scope to Dream source ids is defined and tested
- [ ] Enrichment queue supports retry (max 5 attempts), poison disposition, and stale lease recovery
- [ ] `DocumentIngestionToolSettings.EnrichmentEnabled` toggle controls enrichment activation
- [ ] Startup validation rejects invalid enrichment configuration

### Phase 07 — Learning Pipeline
- [ ] `LeanKernel.Services.Learning` project is a standalone background service with its own DI and configuration
- [ ] `LeanKernel.Services.Learning` references `LeanKernel.Core`, `LeanKernel.Logic`, `LeanKernel.Data`, `LeanKernel.Services.Common` — NOT `LeanKernel.Services.Gateway`
- [ ] Turn-event queue is bounded and non-blocking; completed turns enqueue without slowing the response
- [ ] Turn-event queue recovers missed events from `IEventStore` on startup (replays since last checkpoint)
- [ ] `LearningBackgroundWorker` drains the queue and runs fact/intent/gap/engagement steps in order
- [ ] Fact extraction delegates to `FactExtractionService` (resolved via keyed `IChatClient("fact-extraction")` registered in Learning DI)
- [ ] Identity-intent extraction produces intent records in memory with scope-relative keys
- [ ] Capability-gap detection logs actionable diagnostics for unmet tool/action requests
- [ ] Engagement tracking computes turn-level signals
- [ ] `KnowledgePageUpdateCoordinator` writes back using scope-relative keys via `IMemoryService` (resolved via shared `AddGBrainServices` helper)
- [ ] All pipeline steps are idempotent and safe to retry on worker restart

### Phase 07 — Onboarding Intelligence
- [ ] `OnboardingGapDetector` consumes learned identity intent and produces `OnboardingGap` records
- [ ] `OnboardingDirectiveBuilder` converts gaps into natural-language directives scoped to identity
- [ ] Directives are persisted as memory pages under `onboarding/directive/{GapType}` keys

### Phase 07 — Scheduler
- [ ] `ScheduledJobEntity` table exists with proper EF configuration and migration
- [ ] `CronScheduleEvaluator` correctly evaluates cron expressions including DST/time-boundary edge cases
- [ ] `JobExecutor` resolves job types and dispatches to registered handlers
- [ ] `SchedulerHostedService` claims eligible jobs atomically and updates schedule after execution
- [ ] `TimeBoundaryService` provides correct window checks and next-window calculations
- [ ] Well-known job types (`DreamCycle`, `OnboardingEvaluation`, `KnowledgeConsolidation`) are registered

### Phase 07 — Dream Cycle
- [ ] `DreamCycleJobHandler` invokes `gbrain dream` via `IGBrainMcpClient` with source-scoped parameters
- [ ] In-memory `SemaphoreSlim(1,1)` per source scope prevents concurrent Dream runs on same scope
- [ ] Lock timeout (default 300s) abandons stale runs; worker marks `TimedOut` and requeues with backoff
- [ ] On active lock contention, job is skipped (`SkippedDueToLock`) and rescheduled with ±10% jitter
- [ ] Dream modes (`full`, `targeted`, `drain`) are dispatched per `ScheduledJobEntity.ConfigurationJson`
- [ ] `DreamRunRecord` entity persists phase-level outcomes (status, totals, failures) with nullable `EnrichmentJobId` FK for reverse correlation traversal
- [ ] Backlog-based Dream triggers from enrichment/ingestion queue depth are configurable and tested

### Testing and Quality
- [ ] Unit tests cover enrichment queue, hosted service, correlation chain, and scope preservation
- [ ] Unit tests cover turn-event queue, pipeline steps, knowledge coordinator, onboarding, scheduler, Dream cycle
- [ ] Integration tests cover end-to-end enrichment, learning, scheduler, and Dream cycle paths
- [ ] Coverage report confirms >= 80% for new/changed code
- [ ] `scripts/quality/sonarqube-scan.sh` completed with no unresolved Blocker/Critical/Major issues
- [ ] Deep-review sub-agent run completed and findings resolved

### Documentation
- [ ] `docs/features/learning-pipeline.md` documents learning pipeline overview, steps, configuration
- [ ] `docs/features/scheduler.md` documents scheduler architecture, cron management, Dream cycle
- [ ] `docs/features/enrichment-pipeline.md` documents enrichment trigger, correlation chain, scope preservation
- [ ] `docs/configuration/appsettings-reference.md` updated with Learning, Scheduler, Enrichment, GBrain:Dream sections
- [ ] `docs/plans/phase-21-channel-document-ingestion/evidence.md` and `exit-criteria.md` updated
- [ ] `docs/plans/index.md` status updated for Phase 21 and Phase 07

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | (agent) | Pending | Implementation and testing |
| Reviewer | | Pending | Requires separate session review |
| Approver | | Pending | Requires human approval |
