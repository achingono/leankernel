# Phase 21 Delta + Phase 07 Activities

## Pre-Phase: Refactor MCP Interfaces to `LeanKernel.Services.Common`

Before enrichment or Learning implementation begins, relocate the MCP client abstraction so that `LeanKernel.Services.Learning` (which must not reference `LeanKernel.Services.Gateway`) can invoke Dream cycles and knowledge write-backs.

0a. **Move `IGBrainMcpClient` and associated DTOs to `LeanKernel.Services.Common.Interfaces`**
    - Move interface + `GBrainException`, `McpToolCallParams`, `McpResult` from Gateway to `src/Services/LeanKernel.Services.Common/Interfaces/`
    - Move MCP implementation classes (`GBrainMcpClient`, `GBrainAuthHandler`, `GBrainMemoryClient`, `GBrainDocumentStoreClient`, etc.) to `LeanKernel.Services.Common.Memory`
    - Move health checks (`GBrainHealthCheck`, `LiteLlmHealthCheck`) to `LeanKernel.Services.Common.HealthChecks`
    - Move `GBrainSettings` to `LeanKernel.Services.Common.Configuration`
    - Keep shared DI wiring (`AddGBrainMemory`, `AddEmbeddingClient`, `AddDocumentStoreClient`, `AddServiceHealthChecks`) in `LeanKernel.Services.Common.Extensions`
    - Update `LeanKernel.Services.Gateway` references to point to new locations
    - No functional change — pure relocation

0b. **Add `IDreamService` abstraction in `LeanKernel.Services.Common.Interfaces`**
    - `IDreamService` with `RunDreamAsync(SourceScope, Mode, CancellationToken)` returning `DreamRunResult`
    - Default implementation delegates to `IGBrainMcpClient`
    - Registration: Gateway registers `IDreamService` → `GbrainDreamService`; `LeanKernel.Services.Learning` uses `IDreamService` without referencing Gateway

0c. **Shared DI helpers**
    - `AddGBrainMemory`, `AddEmbeddingClient`, `AddDocumentStoreClient`, `AddServiceHealthChecks` already live in `LeanKernel.Services.Common.Extensions`
    - Both Gateway and Learning call these shared helpers
    - Gateway calls with `GBrainSettings`; Learning calls with its own config

## Step-By-Step Activities

### A. Phase 21 Delta — Post-Ingestion Enrichment (in `LeanKernel.Logic`)

1. **Define enrichment models and contracts**
   - `DocumentEnrichmentJob` record: `(Guid Id, Guid IngestionJobId, string FilePath, string FileName, string Fingerprint, Guid TenantId, Guid UserId, Guid PersonId, Guid ChannelId, DocumentAvailabilityScope AvailabilityScope, DocumentIngestionSource Source)`
   - `IEnrichmentQueue` interface: `EnqueueAsync(DocumentEnrichmentJob, CancellationToken)`, `TryClaimNextAsync(workerId, leaseDuration, CancellationToken)`, `CompleteAsync(jobId, result, CancellationToken)`, `FailAsync(jobId, error, retryAt, CancellationToken)`, `RecoverStaleLeasesAsync(CancellationToken)`
   - `EnrichmentResult` record: `(Guid IngestionJobId, Guid EnrichmentJobId, Guid? DreamRunId, bool Success)` — durable correlation chain
   - `DocumentEnrichmentRequestedEvent` record implementing `IHasEnvelope`, carrying `IngestionJobId` + scoped identity metadata

2. **Add `EnrichmentJobEntity` to `LeanKernel.Data`**
   - New entity with: `Id`, `IngestionJobId`, `TenantId`, `UserId`, `PersonId`, `ChannelId`, `AvailabilityScope`, `FilePath`, `FileName`, `Fingerprint`, `Status` (Pending/Processing/Completed/Failed/Poisoned), `AttemptCount`, `LastError`, `LeaseOwner`, `LeaseExpiresAt`, `NextAttemptAt`, `DreamRunId`, `CreatedAt`, `UpdatedAt`
   - Add `DbSet<EnrichmentJobEntity>` to `EntityContext`
   - Add EF configuration with identity-partitioning filter, index on `(Status, NextAttemptAt, LeaseExpiresAt)`
   - Generate EF migration

3. **Implement `EnrichmentQueue` (DB-backed)**
   - Same lease/retry pattern as `DocumentIngestionQueue`
   - `EnqueueAsync` writes a `Pending` row
   - `TryClaimNextAsync` atomic lease with expired-lease recovery
   - `CompleteAsync` / `FailAsync` with retry budget (5 attempts before poison)

4. **Emit enrichment event from `DocumentIngestionHostedService` using `IEventStore.AppendAsync`**
   - After successful `queue.CompleteAsync(...)`, directly call `IEventStore.AppendAsync(new DocumentEnrichmentRequestedEvent { ... })` using `IDbContextFactory<EntityContext>` (singleton-safe pattern, same as `DocumentIngestionQueue`)
   - Do NOT use `IEventCollector` — it is request-scoped and unavailable from background services
   - Add `DocumentIngestionToolSettings.EnrichmentEnabled` configuration toggle
   - On startup, call `RecoverStaleLeasesAsync` for enrichment jobs

5. **Add enrichment subscriber (optional — enrichment may bypass subscriber by having the hosted service enqueue directly)**
   - If subscriber path is used: new `IEventSubscriber` that filters for `DocumentEnrichmentRequestedEvent` and enqueues to `IEnrichmentQueue`
   - Alternative: `EnrichmentHostedService` reads enrichment jobs directly from `IEnrichmentQueue` (no subscriber needed); enrichment events serve only as audit trail via `IEventStore`
   - Decision: **Use direct enqueue path** — `DocumentIngestionHostedService` writes to both `IDocumentIngestionQueue.CompleteAsync` and `IEnrichmentQueue.EnqueueAsync` in a single scope

6. **Implement `EnrichmentHostedService`**
   - Background service in `LeanKernel.Logic` co-located with `DocumentIngestionHostedService`
   - Polls `IEnrichmentQueue`, processes enrichment: calls `FactExtractionService` on extracted text, uses `MemoryPageNormalizer`/`MemoryPageLinker` to produce derived artifacts
   - `FactExtractionService` needs keyed `IChatClient("fact-extraction")` — `EnrichmentHostedService` creates a DI scope and resolves it from the host's registered services (same pattern as existing `DocumentIngestionHostedService`)
   - Derived artifacts inherit original `AvailabilityScope` — enforced at write time
   - On startup, call `RecoverStaleLeasesAsync()` to reclaim stale "Processing" jobs after crash

7. **Add source-mapping contract from ingestion scope to GBrain Dream source ids**
   - Mapping function: `MapToDreamSourceId(TenantId, ChannelId, AvailabilityScope)` → string
   - Validates derived artifacts inherit original visibility scope
   - Called by Dream cycle job (Phase 07 item) when enrichment triggers a Dream window

8. **Wire DI for enrichment services**
   - Register `IEnrichmentQueue` → `EnrichmentQueue` (singleton)
   - Register `EnrichmentHostedService` as hosted service
   - Add `ValidateOnStart()` for enrichment settings

### B. Create `LeanKernel.Learning` Project

9. **Scaffold the learning service project**
    - New console/background-service project `src/Services/LeanKernel.Services.Learning/`
    - `LeanKernel.Services.Learning.csproj` referencing:
      - `LeanKernel.Core` (shared contracts, entities, events)
      - `LeanKernel.Logic` (memory services, fact extraction, event contracts)
      - `LeanKernel.Data` (EF context for scheduled-job entities)
      - `LeanKernel.Services.Common` (shared GBrain services, `IDreamService`, `IGBrainMcpClient`)
   - `Program.cs` with hosted service builder, configuration, DI
   - `appsettings.json` with `Learning`, `Scheduler`, `OpenAI`, `GBrain`, `ConnectionStrings` sections

10. **Implement turn-event queue with recovery mechanism**
    - Bounded `TurnEventQueue` (`Channel<CompletedTurnEvent>` with bounded capacity)
    - On startup, recover missed events from `IEventStore` by querying `EventEntity` for `TurnCompletedEvent` records since last checkpoint (store checkpoint as `LastProcessedTurnId` in a simple config/DB record)
    - After recovery, new turn events arrive via in-memory channel
    - `ICompletedTurnEventProducer` for `LeanKernel.Services.Gateway`/Logic to enqueue completed turns
    - `ICompletedTurnEventConsumer` for the learning worker to dequeue

11. **Add `TurnCompletedEvent` and integration with event spine**
    - `TurnCompletedEvent` record: `(Guid TurnId, Guid SessionId, Guid TenantId, Guid UserId, Guid PersonId, Guid ChannelId, IReadOnlyList<TurnMessage> Messages, IReadOnlyList<ToolCallEvent> ToolCalls, TimeSpan Elapsed, DateTimeOffset Timestamp)`
    - Emit `TurnCompletedEvent` at the end of the turn pipeline via `IEventStore.AppendAsync` (not `IEventCollector` — that is already consumed by that point in the request lifecycle)
    - Create a new `TurnCompletionSubscriber` in `LeanKernel.Logic` (registered as `IEventSubscriber`) that persists `TurnCompletedEvent` to the event spine
    - `LeanKernel.Services.Learning` polls `IEventStore` for new `TurnCompletedEvent` records and feeds them into the in-memory channel

### C. Learning Pipeline Steps (`LeanKernel.Services.Learning`)

12. **Implement `LearningBackgroundWorker`**
    - Drains the turn-event queue in order
    - Runs the self-improvement pipeline steps sequentially per turn
    - Creates a scoped DI container for each turn to resolve memory services from `LeanKernel.Logic`
    - Handles cancellation, logging, error isolation per turn

13. **Implement pipeline steps**
    - **Fact extraction**: delegates to `FactExtractionService` from `LeanKernel.Logic`. Requires keyed `IChatClient("fact-extraction")` registration in `LeanKernel.Services.Learning` DI
    - **Identity-intent extraction**: extracts identity cues, preferences, intent from turn messages; writes to memory via `MemoryPageNormalizer`/`MemoryPageLinker`. Uses `IMemoryService` (registered via shared `AddGBrainServices` helper from Activity 0c).
    - **Capability-gap detection**: identifies tool/action requests the agent couldn't satisfy; logs for diagnostics
    - **Engagement tracking**: computes turn-level engagement signals (response helpfulness, follow-up rate)

14. **Implement `KnowledgePageUpdateCoordinator`**
    - Writes learned facts and extracted knowledge back to GBrain-backed memory pages via `IMemoryService`
    - Uses scope-relative keys per memory conventions (no double-prefixing)
    - `IMemoryService` is resolved from the shared helper registered in Activity 0c
    - Idempotent — replaying the same turn produces the same state

### D. Onboarding Intelligence (`LeanKernel.Services.Learning`)

15. **Implement `OnboardingGapDetector`**
    - Consumes learned identity intent from memory (via `IMemoryService`)
    - Detects missing identity data (name, email, timezone, preferences)
    - Produces `OnboardingGap` records with gap type, suggested directive, priority

16. **Implement `OnboardingDirectiveBuilder`**
    - Converts `OnboardingGap` records into natural-language directives injected into the system prompt
    - Directives are scoped to the identity they target
    - Persisted as memory pages under `onboarding/directive/{GapType}` key via `IMemoryService`

### E. Scheduler (`LeanKernel.Services.Learning`)

17. **Implement scheduler entities and repository**
    - `ScheduledJobEntity` in `LeanKernel.Core.Entities`: `Id`, `Name`, `CronExpression`, `JobType`, `ConfigurationJson`, `TenantId`, `Enabled`, `LastRunAt`, `NextRunAt`, `CreatedAt`, `UpdatedAt`
    - Add `DbSet<ScheduledJobEntity>` to `EntityContext`
    - Add EF configuration with index on `(Enabled, NextRunAt)`
    - Add `IRepository<ScheduledJobEntity>` usage for CRUD
    - Generate EF migration (applied by whichever service runs `ApplyMigrationsAndSeedAsync` first)

18. **Implement `CronScheduleEvaluator`**
    - Evaluates cron expressions against `DateTimeOffset.UtcNow`
    - Returns `next` and `previous` fire times using Cronos library (NuGet `Cronos` v0.8+)
    - Handles DST transitions and time-boundary edge cases

19. **Implement `JobExecutor`**
    - Resolves job type from `ScheduledJobEntity.JobType` string
    - Invokes the corresponding handler via DI
    - Supported job types: `DreamCycle`, `OnboardingEvaluation`, `KnowledgeConsolidation`
    - Failures are isolated and logged with actionable context

20. **Implement `SchedulerHostedService`**
    - Background service that evaluates cron schedules on a timer (every 30 seconds)
    - Claims eligible jobs (enabled, next run <= now) via optimistic DB lock
    - Delegates to `JobExecutor`
    - Updates `LastRunAt` and `NextRunAt` after execution

21. **Implement `TimeBoundaryService`**
    - Provides helper methods: `IsWithinWindow(DateTimeOffset, TimeSpan start, TimeSpan end)`, `NextWindowStart(TimeSpan start)`
    - Used by Dream cycle job for bounded execution windows

### F. Dream Cycle Job (`LeanKernel.Services.Learning`)

22. **Implement `DreamCycleJobHandler`**
    - Implements the `IJobHandler` interface for `JobType = "DreamCycle"`
    - Invokes `gbrain dream` via `IDreamService.RunDreamAsync(...)` with source-scoped parameters
    - `IDreamService` is resolved from the shared helper registered in Activity 0b
    - Uses `SemaphoreSlim(1,1)` per Dream source scope as in-memory lock
    - Lock timeout: `DreamLockTimeoutSeconds` (default 300s)
    - On lock contention: marks job as `SkippedDueToLock` and reschedules at next cron interval with ±10% jitter
    - Supports Dream modes: `full`, `targeted`, `drain` — controlled by `ScheduledJobEntity.ConfigurationJson`

23. **Persist Dream run reports**
    - `DreamRunRecord` entity: `Id`, `SourceScope`, `Mode`, `PhaseStatusJson`, `TotalPages`, `FailedPages`, `StartedAt`, `CompletedAt`, `Status`, `EnrichmentJobId` (nullable FK for reverse correlation traversal)
    - Add to `EntityContext`
    - Reports used for diagnostics, replay, and scheduler decisions

24. **Add backlog-based Dream triggers**
    - When `EnrichmentQueue` depth exceeds threshold or ingestion backlog is high, scheduler may trigger a `targeted` Dream cycle
    - Thresholds configurable in `LearningSettings`

### G. Registration and Configuration

25. **Wire DI in `LeanKernel.Services.Learning`**
    - Register `TurnEventQueue`, `LearningBackgroundWorker`, pipeline steps, `KnowledgePageUpdateCoordinator`
    - Register `OnboardingGapDetector`, `OnboardingDirectiveBuilder`
    - Register `CronScheduleEvaluator`, `JobExecutor`, `SchedulerHostedService`
    - Register `TimeBoundaryService`, `DreamCycleJobHandler`
    - Register `IChatClient` with key `"fact-extraction"` pointing at `OpenAI:FactExtraction` config section (use shared helper `AddFactExtractionChatClient` from Activity 13)
    - Call shared `AddGBrainServices` helper (from Activity 0c) to register `IDreamService` and `IMemoryService`
    - Register `EntityContext` with the same connection-string resolution pattern as Gateway
    - Configure `LearningSettings`, `SchedulerSettings`, `OpenAISettings`, `GBrainSettings` from configuration

26. **Wire enrichment DI in `LeanKernel.Logic`**
    - Update `IServiceCollectionExtensions.AddDocumentIngestion` to register `IEnrichmentQueue`, `EnrichmentHostedService`

27. **Add configuration sections**
    - `Agents:Tools:DocumentIngestion:Enrichment` (Enabled, MaxConcurrentJobs, QueueCapacity)
    - `Learning` (Enabled, TurnQueueCapacity, PipelineStepOrder, MaxConcurrency)
    - `Scheduler` (PollIntervalSeconds, DefaultCronExpressions)
    - `Scheduler:Jobs` (list of `ScheduledJobEntity` defaults for well-known jobs)
    - `GBrain:Dream` (LockTimeoutSeconds, DefaultMode, DefaultSourceScope)

28. **Add startup validation**
    - Validate `LearningSettings` on start
    - Validate `SchedulerSettings` on start
    - Validate enrichment settings on start

### H. Testing

29. **Phase 21 delta tests**
    - `EnrichmentQueueTests`: enqueue, claim, complete, fail, retry, poison, stale lease recovery
    - `EnrichmentHostedServiceTests`: job lifecycle, enrichment call on completion, startup recovery
    - `DocumentEnrichmentRequestedEvent` persistence via `IEventStore`
    - Correlation contract tests: `IngestionJobId → EnrichmentJobId → DreamRunId` chain

30. **Phase 07 unit tests**
    - `TurnEventQueueTests`: bounded capacity, backpressure, dequeue ordering
    - `TurnEventQueueRecoveryTests`: startup replay from `IEventStore`
    - `LearningBackgroundWorkerTests`: step ordering, idempotency, error isolation
    - `FactExtractionStepTests`: integration with `FactExtractionService`
    - `IdentityIntentExtractionStepTests`: extraction scope correctness
    - `KnowledgePageUpdateCoordinatorTests`: scope-relative key usage, idempotency
    - `OnboardingGapDetectorTests`: gap detection from learned intent
    - `OnboardingDirectiveBuilderTests`: directive generation
    - `CronScheduleEvaluatorTests`: cron parsing, DST edge cases, next/previous fire times
    - `SchedulerHostedServiceTests`: job claiming, execution, schedule update
    - `TimeBoundaryServiceTests`: window checks, next window calculation
    - `DreamCycleJobHandlerTests`: lock handling, mode dispatch, timeout, retry jitter
    - `DreamRunRecordPersistenceTests`: record creation, status updates

31. **Integration tests**
    - End-to-end enrichment: document ingested → `DocumentEnrichmentRequestedEvent` persisted → `EnrichmentQueue` processes → derived artifacts persisted
    - End-to-end learning: turn completed → event persisted → `LeanKernel.Learning` recovers → pipeline processes facts → knowledge page updated  
    - End-to-end scheduler: cron job fires → `JobExecutor` invokes handler → outcome recorded
    - End-to-end Dream cycle: `DreamCycleJob` fires → `IDreamService.RunDreamAsync` called → `DreamRunRecord` persisted
    - Scope preservation: derived artifacts from enrichment inherit original `AvailabilityScope`

### I. Documentation

32. Add `docs/features/learning-pipeline.md` — learning pipeline overview, steps, configuration
33. Add `docs/features/scheduler.md` — scheduler architecture, cron job management, Dream cycle
34. Add `docs/features/enrichment-pipeline.md` — enrichment trigger, correlation chain, scope preservation
35. Update `docs/configuration/appsettings-reference.md` with new config sections
36. Update `docs/plans/phase-21-channel-document-ingestion/evidence.md` and `exit-criteria.md` to mark enrichment delta complete
37. Update `docs/plans/index.md` status for Phase 21 and Phase 07

### J. Closure

38. Run coverage collection and confirm >= 80% for new/changed code paths
39. Run `scripts/quality/sonarqube-scan.sh` and resolve `Blocker`, `Critical`, and `Major` findings
40. Run deep-review sub-agent and resolve findings
41. Update `evidence.md` and `exit-criteria.md` approval table

## Review Focus
- **MCP contract relocation**: `IGBrainMcpClient` moved to `LeanKernel.Core.Interfaces` before any implementation begins
- **Event emission from background services**: Use `IEventStore.AppendAsync` with `IDbContextFactory<EntityContext>`, NOT `IEventCollector`
- **Learning pipeline runs as a separate service** (`LeanKernel.Services.Learning`) — verify no circular references with `LeanKernel.Services.Gateway`
- **Keyed `IChatClient("fact-extraction")`** must be registered in `LeanKernel.Services.Learning` DI
- **Turn-completion events** must not block the response path; use `IEventStore.AppendAsync` + recovery mechanism
- **Enrichment derived artifacts** must inherit and preserve original `AvailabilityScope` — no scope broadening
- **Correlation chain** `IngestionJobId → EnrichmentJobId → DreamRunId` is durable and queryable; `DreamRunRecord` includes `EnrichmentJobId` back-reference
- **Dream cycle lock** is in-memory — suitable for single-instance; HA deployments need distributed lock (out of scope)
- **Turn-event queue recovery** replays missed events from `IEventStore` on startup
- **Cron evaluation** correct across DST/time-boundary edge cases
- **Learning steps** are idempotent and safe to retry on worker restart
- **Write-back** uses scope-relative keys via `IMemoryService` abstraction (resolved via shared helper)
- **All new entities** have EF migrations generated; migrations applied by whichever service runs first
- **Startup validation** catches misconfiguration before runtime
