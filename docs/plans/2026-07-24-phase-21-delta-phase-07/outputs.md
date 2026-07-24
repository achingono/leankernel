# Phase 21 Delta + Phase 07 Outputs

## Mandatory Outputs

| Output | Description | Format |
|---|---|---|
| `EnrichmentJobEntity` + EF migration | Durable enrichment queue table | C# entity + migration |
| `IEnrichmentQueue` + `EnrichmentQueue` | DB-backed enrichment job queue | C# interface + implementation |
| `EnrichmentHostedService` | Background service draining enrichment queue | C# hosted service |
| `DocumentEnrichmentRequestedEvent` | Event emitted after successful ingestion | C# record |
| Enrichment `IEventSubscriber` | Fans out enrichment events to queue | C# class |
| `LeanKernel.Services.Learning` project | Separate background service project | .NET project |
| `TurnEventQueue` | Bounded non-blocking turn event channel | C# class |
| `LearningBackgroundWorker` | Drains queue, runs self-improvement pipeline | C# background service |
| Pipeline steps (fact, intent, gap, engagement) | Ordered self-improvement steps | C# classes |
| `KnowledgePageUpdateCoordinator` | Scoped write-back to knowledge/memory | C# class |
| `OnboardingGapDetector` + `OnboardingDirectiveBuilder` | Onboarding intelligence | C# classes |
| `ScheduledJobEntity` + EF migration | Scheduler job persistence | C# entity + migration |
| `CronScheduleEvaluator` | Cron expression evaluation | C# class |
| `JobExecutor` | Dispatches scheduled jobs to handlers | C# class |
| `SchedulerHostedService` | Cron-driven background scheduler | C# hosted service |
| `TimeBoundaryService` | Time window helper | C# class |
| `DreamCycleJobHandler` | Dream cycle orchestration | C# class |
| `DreamRunRecord` entity | Dream run outcome persistence | C# entity |
| Configuration sections | `Learning`, `Scheduler`, `GBrain:Dream`, `Agents:Tools:DocumentIngestion:Enrichment` | JSON |
| Unit + integration tests | Coverage >= 80% | C# tests |
| Feature docs | `docs/features/learning-pipeline.md`, `scheduler.md`, `enrichment-pipeline.md` | Markdown |
| Updated `appsettings-reference.md` | New config sections documented | Markdown |

## Optional Outputs
- CLI management commands for scheduled jobs
- In-memory dashboard for enrichment/scheduler job status

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
- [ ] Coverage report confirms >= 80%
- [ ] SonarQube scan passes with no Blocker/Critical/Major issues
- [ ] Deep review findings resolved
