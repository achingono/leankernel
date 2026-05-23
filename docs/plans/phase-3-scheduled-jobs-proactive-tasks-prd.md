# PRD: Phase 3 Scheduled Jobs and Proactive Tasks

## Overview

Implement the first runtime scheduler for the current LeanKernel solution so the Gateway can execute disabled-by-default cron jobs, proactive agent prompts, knowledge refresh tasks, and maintenance work through the same runtime boundaries used for user turns.

This slice adds a new `LeanKernel.Scheduler` project, configuration under `LeanKernel:Scheduler`, persisted execution history, and deterministic background execution with bounded concurrency.

## Problem Statement

LeanKernel already supports request/response turns, bounded learning work, and durable persistence, but it does not yet have a built-in scheduler that can:

- evaluate cron expressions on a timer,
- execute proactive work without an inbound user message,
- persist scheduler execution history for audit/debugging, and
- shut down cleanly without orphaning in-flight jobs.

Phase 3 needs a lightweight scheduler that matches current architecture constraints:

- use the existing `IAgentRuntime.RunTurnAsync` interface,
- reuse `IKnowledgeService` rather than inventing a second knowledge runtime,
- integrate with the existing EF Core `LeanKernelDbContext`, and
- remain disabled by default.

## Goals

- Add `SchedulerConfig` and strongly typed scheduled job definitions to `LeanKernel.Abstractions`.
- Add a persisted execution record model plus EF Core storage for job runs.
- Create a new `LeanKernel.Scheduler` package for cron evaluation, time-boundary helpers, job execution, and hosted background ticking.
- Execute proactive agent prompts through `IAgentRuntime.RunTurnAsync`.
- Support simple knowledge refresh and maintenance job types.
- Enforce max-concurrency limits and graceful shutdown.
- Document the feature and default configuration.

## Non-Goals

- Chat-based scheduler CRUD management.
- DAG/dependency orchestration between jobs.
- A full migration framework beyond the repo's current `EnsureCreated` model.
- A separate queue or durable job definition store outside app configuration.

## Scope Decisions

- Scheduler definitions live in `LeanKernel:Scheduler:Jobs` configuration for this slice.
- Execution history is durable in PostgreSQL via EF Core.
- Duplicate firing prevention uses both in-memory state during process lifetime and persisted execution history during due checks.
- Hosted-service execution creates a new DI scope per job run.
- Invalid cron expressions or timezones fail the job safely and log actionable errors.

## Functional Requirements

### FR-1 Configuration Contracts

Add:

- `SchedulerConfig`
- `ScheduledJobDefinition`
- `LeanKernelConfig.Scheduler`

Requirements:

- `Enabled=false` by default.
- Tick interval and max concurrency must clamp to safe minimums at runtime.
- Each job definition includes name, cron expression, job type, enable flag, optional prompt/channel/user values, and free-form parameters.

### FR-2 Scheduled Job Execution Model

Add `ScheduledJobExecution` to abstractions for runtime/result reporting with:

- job name,
- scheduled time,
- started/completed timestamps,
- success flag,
- result/error payloads,
- computed duration.

### FR-3 Persistence

Add `ScheduledJobEntity` plus `DbSet<ScheduledJobEntity>` to `LeanKernelDbContext`.

Requirements:

- persist every attempted execution,
- index by job name and scheduled time for efficient duplicate/run-history lookup,
- support querying the latest execution for a job.

### FR-4 Cron Evaluation

`CronScheduleEvaluator` must:

- parse cron expressions with Cronos,
- resolve timezones safely,
- calculate next occurrences,
- determine whether a job is due for the current tick window, and
- avoid re-firing the same scheduled instant when the process restarts.

### FR-5 Time Boundary Awareness

`TimeBoundaryService` must provide:

- named boundaries (`Morning`, `Afternoon`, `Evening`, `Night`),
- timezone-aware local evaluation,
- boundary-start calculation for the current day.

This enables proactive jobs to align with start-of-day or similar temporal boundaries.

### FR-6 Job Execution

`JobExecutor` must support:

1. `agent-prompt`
   - create a `LeanKernelMessage` using configured prompt, channel, and user,
   - attach scheduler metadata,
   - invoke `IAgentRuntime.RunTurnAsync`.
2. `knowledge-refresh`
   - if a `key` parameter is supplied, rehydrate the page via `GetPageAsync` and write it back with `PutPageAsync`,
   - otherwise run a search using `query`/`Prompt` and record the matched keys as the result.
3. `maintenance`
   - delete aged `DiagnosticEntryEntity` rows when `task=cleanup-old-diagnostics`,
   - delete aged `CompactionMarkerEntity` rows when `task=cleanup-compaction-markers`,
   - support `task=cleanup-all` to run both.

Requirements:

- each execution persists success/failure,
- all execution paths are cancellation-aware,
- failures surface descriptive error text.

### FR-7 Hosted Scheduler Loop

`SchedulerHostedService` must:

- start only when enabled,
- tick on a configurable interval,
- evaluate all enabled jobs each tick,
- create a scope per execution,
- limit concurrency with `SemaphoreSlim`,
- stop accepting new work during shutdown,
- attempt to drain in-flight jobs before cancellation.

### FR-8 Dependency Injection and Gateway Wiring

Add `AddLeanKernelScheduler(this IServiceCollection, SchedulerConfig)` that:

- no-ops when disabled,
- registers config/options,
- registers evaluator/time-boundary services as singletons,
- registers `JobExecutor` as scoped,
- registers the hosted service.

The Gateway must reference the new project and call registration after persistence/knowledge/agents are wired.

### FR-9 Tests

Add unit coverage for:

- cron parsing and due calculations,
- invalid cron/timezone handling,
- boundary detection,
- job execution success/failure paths,
- maintenance cleanup behavior,
- hosted-service tick scheduling, duplicate suppression, concurrency limiting, and shutdown drain behavior.

## Architecture Notes

### Existing Runtime Alignment

- Use `IAgentRuntime.RunTurnAsync`, not the outdated `ProcessTurnAsync` signature.
- Use `IDbContextFactory<LeanKernelDbContext>` inside scheduler components to avoid cross-scope DbContext reuse.
- Use explicit service scopes from the hosted service for each job execution.

### Duplicate Suppression Strategy

Due checks should not rely only on in-memory last-run timestamps. The evaluator/executor path should also inspect persisted execution history so a restart does not re-run the same scheduled occurrence.

### Schema Caveat

The repository currently uses `Database.EnsureCreatedAsync()` and does not include migrations. This implementation therefore targets new schema creation through the existing boot path; operational follow-up may still be required for already-provisioned databases.

## Implementation Plan

1. Add abstractions and persistence contracts.
2. Add the new scheduler project and DI extension surface.
3. Implement cron evaluation and time-boundary helpers.
4. Implement job execution and persisted execution recording.
5. Implement the hosted service loop with bounded concurrency and graceful shutdown.
6. Wire the Gateway and appsettings defaults.
7. Add unit tests.
8. Update README and scheduler feature documentation.

## Review Summary

The implementation plan was reviewed with a second model before coding. Review outcomes incorporated into this PRD:

- use persisted execution history, not only in-memory state, for restart-safe duplicate suppression;
- create per-job DI scopes from the hosted service;
- validate timezone IDs and cron expressions explicitly;
- make scheduler tick behavior testable with small overridable helpers rather than hard-coding difficult-to-test timing behavior;
- treat knowledge-refresh writeback as best-effort and document that it depends on current `IKnowledgeService` semantics.

## Risks and Dependencies

- Existing deployments using a pre-scheduler database schema may need operational remediation because `EnsureCreated` does not evolve an existing schema.
- Timezone IDs must match the runtime OS timezone database.
- Knowledge refresh is limited by current `IKnowledgeService` capabilities.
- Scheduler jobs that call the runtime may contend with user traffic if max concurrency is set too high.

## Acceptance Criteria

- `LeanKernel:Scheduler` exists in configuration and is disabled by default.
- The solution contains a new `LeanKernel.Scheduler` project referenced by Gateway.
- Enabled jobs execute on cron schedules with bounded concurrency.
- `agent-prompt` jobs invoke the runtime through `IAgentRuntime.RunTurnAsync`.
- `knowledge-refresh` and `maintenance` jobs execute and persist success/failure.
- Every execution is stored in persistence for audit/history.
- Scheduler shutdown waits for active jobs before forcing cancellation.
- README and feature docs describe the new scheduler behavior accurately.
