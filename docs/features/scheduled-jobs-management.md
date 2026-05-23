# Scheduled Jobs Management

LeanKernel now supports disabled-by-default scheduled jobs through the `LeanKernel.Scheduler` runtime package.

## Highlights

- Cronos-based cron evaluation from `LeanKernel:Scheduler:Jobs`.
- Proactive `agent-prompt` jobs run through the same `IAgentRuntime` path used for user turns.
- `knowledge-refresh` jobs can rehydrate knowledge pages by key or refresh search matches.
- `maintenance` jobs can clean old diagnostics and compaction markers.
- Bounded concurrency via `MaxConcurrentJobs`.
- Durable execution history persisted to the `ScheduledJobExecutions` table.
- Graceful shutdown that stops accepting new jobs and waits for active jobs to finish.

## Configuration

Configure the scheduler in `src/LeanKernel.Gateway/appsettings.json` under `LeanKernel:Scheduler`:

- `Enabled`
- `TickIntervalSeconds`
- `MaxConcurrentJobs`
- `DefaultTimezone`
- `Jobs[]`

Each job supports:

- `Name`
- `CronExpression`
- `JobType` (`agent-prompt`, `knowledge-refresh`, `maintenance`)
- `Prompt`
- `ChannelId`
- `UserId`
- `Enabled`
- `Parameters`

## Supported Parameters

Common parameters include:

- `timezone` — override the default timezone for cron evaluation.
- `required_boundary` or `time_boundary` — only execute when the local day boundary matches `Morning`, `Afternoon`, `Evening`, or `Night`.

Maintenance-specific parameters:

- `task` — `cleanup-old-diagnostics`, `cleanup-compaction-markers`, or `cleanup-all`.
- `retention_days` — retention window for cleanup.

Knowledge-refresh-specific parameters:

- `key` — refresh one page by key.
- `query` — search for pages to refresh.
- `max_results` — cap the number of search matches refreshed.

## Persistence and Reliability

The scheduler keeps lightweight runtime state in memory to avoid double-firing within a process lifetime and also checks persisted execution history so a restart does not replay the same scheduled occurrence. Every attempt records:

- scheduled time,
- start/completion timestamps,
- success/failure,
- result text,
- error text.

## Execution Model

`SchedulerHostedService` ticks on the configured interval, evaluates all enabled jobs, and creates a new DI scope for each execution. That keeps scheduled work aligned with existing scoped services such as the agent runtime and persistence abstractions.
