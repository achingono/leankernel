# Intelligent Brain Integration: EnrichmentPipeline <> LearningPipeline

## Overview
Integration strategy for connecting the Phase 21 enrichment pipeline with the Phase 07 learning pipeline to enable end-to-end AI model lifecycle management with durable correlation and intelligent orchestration.

## Key Integration Points

### 1. Event-Based Enrichment Trigger
**From Phase 21**: `DocumentEnrichmentRequestedEvent` emitted via `IEventStore.AppendAsync` after successful ingestion

**Integration**: The `EnrichmentHostedService` in `LeanKernel.Logic` processes enrichment jobs and invokes Dream cycle orchestration via `IDreamService`

**Pipeline Flow**:
```
DocumentIngestionHostedService (Phase 21)
  ↓ enqueues
IEnrichmentQueue (DB-backed)
  ↓ processes (EnrichmentHostedService)
  ↓ emits
DocumentEnrichmentRequestedEvent (IEventStore)
  ↓
IDreamService.RunDreamAsync (via Scheduler) ← Phase 07 Dream Cycle Job
```

### 2. Learning Pipeline as Separate Service
**From Phase 07**: `LeanKernel.Services.Learning` - standalone background service with:
- Turn-event queue (bounded, non-blocking)
- Fact/intent/gap/engagement processing pipeline
- Knowledge page updates
- Onboarding intelligence
- Cron scheduler with DreamCycleJob

**Integration Benefits**:
- Resource isolation from Gateway/Logic
- Independent scaling capabilities
- Dedicated background worker for asynchronous processing
- Can run Dream cycles independently of request latency

### 3. Correlation Chain Infrastructure
**Durable Correlation** via composite keys:
```
Phase 21: IngestionJobId → EnrichmentJobId
IEnrichmentQueue (DB-backed)
                  ↓
Phase 21/07: EnrichmentJobId → DreamRunId (via DreamRunRecord)
DreamRunRecord (Phase 07 scheduler)
                  ↓
Phase 07: Enriched fact entities persist via MemoryService
                   ↓
Phase 07: LearningPipeline writes facts to knowledge pages
```

### 4. Scope Preservation Mechanism
**Identity Partitioning**: All derived artifacts inherit original availability scope through:
- `AvailabilityScope` enum preserved in `EnrichmentJob`
- `IMemoryService` with scope-relative key usage
- No double-prefixing in knowledge page keys

**Write-Time Enforcement**:
```
EnrichmentHostedService
  ↓ uses original AvailabilityScope
  ↓ writes to memory via MemoryPageNormalizer
  ↓ creates derived artifacts with scope metadata
  ↓ LearningPipeline coordinator respects scope boundaries
```

## Implementation Details

### Enrichment Service References
The `EnrichmentHostedService` depends on:
- `LeanKernel.Logic.Memory` (for fact extraction services)
- `LeanKernel.Data.EntityContext` (for lease tracking)
- `GBrainDreamService` (via `IDreamService` abstraction)
- Optionally: `FactExtractionService` (for processing enriched content)

### Learning Service References
The `LeanKernel.Services.Learning` project:
- References `LeanKernel.Core` for shared contracts
- References `LeanKernel.Logic` for memory/fact services
- References `LeanKernel.Services.Common` for `IDreamService` and `IGBrainMcpClient`
- **Does NOT** reference `LeanKernel.Services.Gateway` (enforced by project architecture)

## Technical Pattern: Singleton-Safe DB Access

Both enrichment and learning services use this pattern to safely access EntityContext across multiple workers:

```csharp
// EnrichmenteHostedService
using IDbContextFactory<EntityContext> in each operation

// LearningBackgroundWorker  
using IDbContextFactory<EntityContext> in sequential operations
```

This ensures thread-safe EF Core access when persistence is required for background processing.

## State Management

### Checkpoint-Based Recovery
**Phase 07 Learning**: Uses `LearningCheckpointEntity` for turn-event replay:
```
LearningBackgroundWorker
  ├─ Recovers missed turn-completed events on startup
  ├─ Stores checkpoint: LastProcessedCreatedOnUtc, LastProcessedEventRowId
  └─ Replays events in order before draining queue
```

### Lease-Based Queue Processing
**Phase 21 Enrichment**: Uses leased queues for durability:
```
EnrichmentQueue
  ├─ Atomic lease: UPDATE "EnrichmentJobs" SET LeaseOwner = ?, LeaseExpiresAt = ?
  ├─ Stale lease recovery on startup
  └─ Poison queue with retry budget
```

## Service Composition Benefits

### Scalability Isolation
- **Phase 21**: enrichment runs in `LeanKernel.Logic` (request-adjacent, light processing)
- **Phase 07**: learning runs as separate `LeanKernel.Services.Learning` service (heavy processing, independent scaling)

### Architecture Boundaries
```
LeanKernel.Gateway (Transport, Response)
       ↓
LeanKernel.Logic (TurnPipeline, Tools)
       ├─ DocumentIngestion (Phase 21)
       └─ TurnCompletion, Tools, Memory (Phase 07 inputs)
       ↓
LeanKernel.Services.Learning (Async Background Service)
       ├─ Learning Pipeline (Phase 07)
       ├─ Scheduler + Dream (Phase 07)
       └─ Optional: Dream orchestration coordination
```

## Data Flow Diagram

```
Channel Attachment 
  ↓
DocumentIngestionHostedService (Phase 21)
  ↓ enqueues
DocumentIngestionQueue (DB)
  ↓ processes
  ↓ writes enrichment jobs
  ↓ enqueues to
IEnrichmentQueue (DB) ← Phase 21 Delta
  ↓ processes (EnrichmentHostedService)
  ↓ emits
DocumentEnrichmentRequestedEvent
  ↓
Scheduler (Phase 07)
  ↓
DreamCycleJobHandler (Phase 07)
  ↓
GBrain Dream Cycle

Concurrent with above:
Turn Pipeline (Phase 03)
  ↓
TurnCompletedEvent emitted
  ↓ enqueues
TurnEventQueue (Channel)
  ↓
LearningBackgroundWorker (Phase 07)
  ↓
  ├─ Fact Extraction → KnowledgePageUpdateCoordinator
  ├─ Identity Intent → Memory storage
  ├─ Capability Gap → Diagnostic logs
  └─ Engagement Tracking → Metrics

Both paths write to:
  • IEventStore (durable audit trail)
  • GBrain-backed memory pages
  • Shared EntityContext (for job state)
```

## Testing Strategy

### Integration Tests (Existing)
1. **Document Ingestion → Enrichment pipeline**: Verify `DocumentEnrichmentRequestedEvent` emitted correctly
2. **Learning Pipeline**: Verify turn events processed and facts written back
3. **Scheduler**: Verify cron jobs execute correctly
4. **Dream Coordination**: Verify enrichment-triggered Dream cycles

### Cross-Service Tests
1. **Correlation chain**: Verify `IngestionJobId → EnrichmentJobId → DreamRunId` links persist correctly
2. **Scope inheritance**: Verify scope boundaries enforced across service boundaries
3. **State recovery**: Verify both enrichment and learning services recover correctly after restart

## Performance Considerations

### Queue Backpressure
- **Turn-event queue**: Bounded (configurable, default: 1000)
- **Enrichment queue**: Bounded (configurable, default: 100)
- **Backpressure handling**: Drop oldest events, process at high priority

### Resource Isolation
- **Learning service**: Separate process (Docker container) with dedicated CPU/memory
- **Enrichment**: Runs in same process as Logic (lighter processing)
- **Dream cycles**: Scheduled independently, can scale horizontally

## Configuration Structure

### Phase 21 Delta
```json
"Agents:Tools:DocumentIngestion": {
  "Enabled": true,
  "EnrichmentEnabled": true,
  "MaxConcurrentJobs": 3,
  "QueueCapacity": 100,
  "Enrichment": {
    "Enabled": true,
    "MaxConcurrentJobs": 3,
    "QueueCapacity": 100,
    "LeaseTimeoutMinutes": 5
  }
}
```

### Phase 07 Learning Service
```json
"Learning": {
  "Enabled": true,
  "TurnQueueCapacity": 1000,
  "MaxConcurrency": 1,
  "PipelineStepOrder": ["Fact", "IdentityIntent", "GapDetection", "Engagement"],
  "MaxMemoryUsageMB": 512
},
"Scheduler": {
  "Enabled": true,
  "PollIntervalSeconds": 30,
  "DreamLockTimeoutSeconds": 300,
  "DefaultDreamMode": "full",
  "Jobs": [
    { "Name": "DreamCycle", "Cron": "0 */6 * * *", "Enabled": true },
    { "Name": "OnboardingEvaluation", "Cron": "0 2 * * *", "Enabled": true }
  ]
}
```

## Migration Path

### Current State Analysis
- Phase 21 enrichment: Implemented, but missing Dream coordination triggers
- Phase 07 learning: Implemented as `LeanKernel.Services.Learning`
- Missing: Integration tests for cross-service correlation

### Required Changes
1. **Add enrichment-driven Dream triggers**: Connect enrichment completion to scheduler for targeted Dream runs
2. **Implement cross-service correlation**: Ensure `EnrichmentJobId → DreamRunId` links persist correctly
3. **Add integration tests**: Verify enrichment and learning services interoperate correctly
4. **Update documentation**: Document the real-time data flow between services

## Success Metrics

### Technical
- 100% durable correlation chain: `IngestionJobId → EnrichmentJobId → DreamRunId`
- <500ms latency between enrichment completion and Dream window opening
- 0% scope broadening in derived artifacts
- 100% idempotent behavior across service restarts

### Operational
- Enrichment pipeline 99.9% uptime
- Learning pipeline 99.95% uptime (separate scaling)
- No blocking of request processing by background services
- Automatic recovery from crashes with state persistence

## Future Roadmap

### Phase 21 Completion
- Add `DreamCycleJobTrigger` from enrichment backlog depth
- Implement `Source-to-Dream source id` mapping contract
- Add `DocumentEnrichmentRequestedEvent` subscriber for immediate enrichment

### Phase 07 Completion
- Add `OnboardingEvaluationJob` as well-known scheduled job
- Implement `KnowledgeConsolidationJob` for memory pruning
- Add `MemoryPageCompactionJob` for storage optimization

### Phase 21 + 07 Combined
- Implement `MemoryUsageBasedDreamScaling` triggers
- Add `ErrorPropagation` from enrichment to learning pipeline
- Implement `TargetedDreamWindow` based on ingestion backlog

## Conclusion

The current architecture provides a solid foundation for intelligent brain operations through clear architectural boundaries and durable service isolation. The Phase 21 enrichment pipeline and Phase 07 learning pipeline are well-separated concerns that can evolve independently while maintaining critical correlation chains for advanced AI functionality.

The integration pattern should be maintained as a reference for future phase integrations (Phase 22, 23, etc.) to ensure consistency in the system's approach to asynchronous processing and intelligent orchestration.
