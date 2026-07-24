# Phase 21 Delta + Phase 07 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Enrichment job entity + migration | `src/Common/LeanKernel.Data/...` + `src/Common/LeanKernel.Core/Entities/EnrichmentJobEntity.cs` | Added in this phase |
| Enrichment queue | `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/EnrichmentQueue.cs` | DB-backed with lease/retry |
| Enrichment hosted service | `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/EnrichmentHostedService.cs` | Background service co-located with ingestion |
| Enrichment event + subscriber | `src/Common/LeanKernel.Core/Events/DocumentEnrichmentRequestedEvent.cs`, `src/Common/LeanKernel.Logic/Events/EnrichmentSubscriber.cs` | Event spine integration |
| `LeanKernel.Services.Learning` project | `src/Services/LeanKernel.Services.Learning/` | Separate background service |
| Turn event queue | `src/Services/LeanKernel.Services.Learning/TurnEventQueue.cs` | Bounded non-blocking channel |
| Learning background worker | `src/Services/LeanKernel.Services.Learning/LearningBackgroundWorker.cs` | Drains queue, runs pipeline |
| Self-improvement pipeline steps | `src/Services/LeanKernel.Services.Learning/Steps/` | Fact, intent, gap, engagement |
| Knowledge page update coordinator | `src/Services/LeanKernel.Services.Learning/KnowledgePageUpdateCoordinator.cs` | Scope-correct write-back |
| Onboarding gap detector | `src/Services/LeanKernel.Services.Learning/Onboarding/OnboardingGapDetector.cs` | Gap detection |
| Onboarding directive builder | `src/Services/LeanKernel.Services.Learning/Onboarding/OnboardingDirectiveBuilder.cs` | Directive generation |
| Scheduled job entity | `src/Common/LeanKernel.Core/Entities/ScheduledJobEntity.cs` | Scheduler job persistence |
| Cron schedule evaluator | `src/Services/LeanKernel.Services.Learning/Scheduler/CronScheduleEvaluator.cs` | Cron expression evaluation |
| Job executor | `src/Services/LeanKernel.Services.Learning/Scheduler/JobExecutor.cs` | Job handler dispatch |
| Scheduler hosted service | `src/Services/LeanKernel.Services.Learning/Scheduler/SchedulerHostedService.cs` | Cron-driven scheduling |
| Time boundary service | `src/Services/LeanKernel.Services.Learning/Scheduler/TimeBoundaryService.cs` | Time window helpers |
| Dream cycle job handler | `src/Services/LeanKernel.Services.Learning/Scheduler/DreamCycleJobHandler.cs` | Dream orchestration |
| Dream run record entity | `src/Common/LeanKernel.Core/Entities/DreamRunRecord.cs` | Dream outcome persistence |
| Enrichment configuration | `src/Common/LeanKernel.Logic/Configuration/DocumentIngestionToolSettings.cs` (EnrichmentEnabled, etc.) | Extended settings |
| Learning configuration | `src/Services/LeanKernel.Services.Learning/appsettings.json` | Learning + scheduler settings |
| Feature docs | `docs/features/learning-pipeline.md`, `docs/features/scheduler.md`, `docs/features/enrichment-pipeline.md` | New documentation |
| Updated appsettings reference | `docs/configuration/appsettings-reference.md` | Extended config reference |
| Phase 21 docs update | `docs/plans/phase-21-channel-document-ingestion/evidence.md`, `exit-criteria.md` | Delta closure |

## Verification Results

| Check | Result | Notes |
| --- | --- | --- |
| Full solution build | | |
| Unit tests | | |
| Code coverage | | |
| Full-solution test suite | | |
| SonarQube scan | | |
| Deep review | | |
