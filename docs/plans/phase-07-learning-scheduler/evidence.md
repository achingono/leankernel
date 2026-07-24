# Phase 07 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Source learning worker | `~/source/repos/leankernel/src/LeanKernel.Learning/{LearningBackgroundWorker,SelfImprovementPipeline,TurnEventQueue}.cs` | Behavioral reference |
| Source learning steps | `~/source/repos/leankernel/src/LeanKernel.Learning/{FactExtractionStep,IdentityIntentExtractionStep,CapabilityGapDetectionStep,EngagementTrackingStep}.cs` | Behavioral reference |
| Source write-back | `~/source/repos/leankernel/src/LeanKernel.Learning/KnowledgePageUpdateCoordinator.cs` | Behavioral reference |
| Source onboarding | `~/source/repos/leankernel/src/LeanKernel.Context/Identity/{OnboardingGapDetector,OnboardingDirectiveBuilder}.cs` | Behavioral reference |
| Source scheduler | `~/source/repos/leankernel/src/LeanKernel.Scheduler/{CronScheduleEvaluator,JobExecutor,SchedulerHostedService,TimeBoundaryService}.cs` | Behavioral reference |
| Rebuild memory conventions | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`, `src/Services/LeanKernel.Gateway/Providers/GBrainMemoryClient.cs` | Scope-key convention |
| GBrain Dream behavior reference | `https://github.com/garrytan/gbrain/blob/ca04874c8fd6601d5c6f6338d5890aac9de945d8/src/commands/dream.ts` | Native Dream orchestration semantics |
| Runtime Dream bootstrap defaults | `config/gbrain/start-gbrain.sh`, `docker-compose.yml` | LiteLLM-compatible `models.dream.*` initialization |
