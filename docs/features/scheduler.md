# Scheduler

`LeanKernel.Services.Learning` contains a cron-based scheduler for proactive jobs,
including Dream cycle orchestration.

## Runtime Flow

1. `SchedulerHostedService` polls due jobs from `ScheduledJobs`.
2. `JobExecutor` dispatches jobs by `JobType`.
3. `DreamCycleJobHandler` acquires a per-scope in-memory lock and invokes Dream.
4. `DreamRunRecord` persists run outcomes.

## Components

- `src/Services/LeanKernel.Services.Learning/Scheduler/SchedulerHostedService.cs`
- `src/Services/LeanKernel.Services.Learning/Scheduler/JobExecutor.cs`
- `src/Services/LeanKernel.Services.Learning/Scheduler/CronScheduleEvaluator.cs`
- `src/Services/LeanKernel.Services.Learning/Scheduler/DreamCycleJobHandler.cs`
- `src/Services/LeanKernel.Services.Common/Memory/GBrainDreamService.cs`
- `src/Common/LeanKernel.Core/Entities/ScheduledJobEntity.cs`
- `src/Common/LeanKernel.Core/Entities/DreamRunRecord.cs`

## Configuration

- `Scheduler:Enabled`
- `Scheduler:PollIntervalSeconds`
- `Scheduler:DreamLockTimeoutSeconds`
- `Scheduler:DefaultDreamMode`
