# Phase 07 Learning And Scheduler

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add background self-improvement and time-based automation to the rebuild: a post-turn learning pipeline that mines completed turns for facts, identity intent, capability gaps, and engagement signals and writes results back into knowledge/memory; identity-onboarding gap detection and directive building driven by learned intent; and a scheduler that runs cron-defined proactive jobs. This ports the source repo's `LeanKernel.Learning`, identity-onboarding pieces from `LeanKernel.Context`, and `LeanKernel.Scheduler`.

## Scope
This phase covers asynchronous, out-of-band processing that reacts to turns and time, plus the onboarding intelligence that depends on learning. It reuses the memory pipeline, knowledge service, and identity partitioning. It does not cover the synchronous turn pipeline internals (Phase 03), model routing (Phase 04), tools (Phase 05), channels (Phase 06), diagnostics persistence/UI (Phase 08), or Blazor UI (Phase 09).

## In Scope
- A turn-event queue that enqueues completed turns for asynchronous processing without blocking the response.
- A learning background worker running an ordered self-improvement pipeline of steps: fact extraction, identity-intent extraction, capability-gap detection, and engagement tracking.
- A knowledge-page update coordinator that writes learned facts/knowledge back to the wiki/knowledge and memory stores under correct scope.
- Identity onboarding intelligence: onboarding gap detection and directive building that inject onboarding prompts when identity information is missing, driven by extracted intent.
- A scheduler: cron schedule evaluation, a job executor, a scheduler hosted service, time-boundary logic, and scheduled-job entities/repository with management surfaces.
- Configuration for learning enablement, step toggles, queue bounds, and scheduler jobs; startup validation.
- Tests for queue backpressure, step ordering/idempotency, scope-correct write-back, cron evaluation, and job execution.

## Out of Scope
- The synchronous turn pipeline (Phase 03) and model routing (Phase 04).
- Admin/onboarding UI (Phase 09) — this phase delivers the runtime/services only.
- Diagnostics persistence and metrics for learning/scheduler (Phase 08), beyond emitting signals.

## Entry Criteria
- Memory pipeline, knowledge service, and identity partitioning are operational.
- A turn-completion hook exists (Phase 03 pipeline event emission preferred) to feed the turn-event queue.
- Source references captured as behavioral targets: `~/source/repos/leankernel/src/LeanKernel.Learning/{LearningBackgroundWorker,SelfImprovementPipeline,TurnEventQueue,FactExtractionStep,IdentityIntentExtractionStep,CapabilityGapDetectionStep,EngagementTrackingStep,KnowledgePageUpdateCoordinator}.cs`; `src/LeanKernel.Context/Identity/{OnboardingGapDetector,OnboardingDirectiveBuilder,IdentityProvider,IdentityUpdateProjector}.cs`; `src/LeanKernel.Scheduler/{CronScheduleEvaluator,JobExecutor,SchedulerHostedService,TimeBoundaryService}.cs`; `src/LeanKernel.Persistence/Entities/ScheduledJobEntity.cs`.

## Exit Criteria
Completed turns are asynchronously mined for facts/intent/gaps/engagement and written back under correct scope, onboarding gaps produce directives from learned intent, and cron-defined jobs run on schedule. See `exit-criteria.md`.

## Design Delta: Intelligent Brain Track
- Add a scheduler-owned `DreamCycleJob` that invokes native `gbrain dream` phases instead of re-implementing Dream semantics in C#.
- Add cadence and bounded-window controls for Dream (`full`, `targeted`, `drain`) with lock-aware retry behavior.
- Add explicit source scoping for Dream runs so freshness markers and maintenance outputs are attributable to a concrete source.
- Add policy for when ingestion backlog should trigger Dream windows (time-based and queue-depth-based thresholds).
- Persist Dream run reports and phase outcomes to runtime records for diagnostics and automated replay.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
