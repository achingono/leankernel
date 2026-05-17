# PRD: Scheduled Job Management and Chat CRUD Tooling

## Overview

Implement full scheduled-job management in LeanKernel with a built-in chat tool for CRUD operations, durable runtime storage, scoped defaults, admin governance, and compatibility with OpenClaw runtime job capabilities.

This plan shifts focus from “import-only” to implementing the complete scheduler management functionality LeanKernel needs.

## Problem Statement

LeanKernel currently has:

- static scheduler registration via code (`ProactiveTaskRunner`)
- a minimal `ReminderTool` that only schedules jobs and does not support read/update/delete or durable ownership/state

It does not yet provide a first-class, chat-driven scheduler management surface.

OpenClaw evidence confirms production needs beyond simple cron creation:

- per-job metadata (agent/session/channel targeting, timezone, delivery settings)
- durable execution state (next/last run, status, errors, retries)
- one-time and recurring schedules
- observable failures (missing conversation reference, delivery failures, timeout)

## Scope Decisions (Confirmed)

- Implementation path: **built-in tool** in `LeanKernel.Plugins` (not runtime skill).
- Default scope: jobs created from chat are scoped to **current session user + channel**.
- Elevation: global/admin scope requires explicit intent.
- Admins: can manage all jobs.

## Goals

- Add a built-in multi-operation tool for chat-based scheduled job CRUD.
- Introduce scheduler management service and persistent runtime job store.
- Support both recurring cron and one-time schedules with per-job timezone.
- Enforce scope-aware authorization for non-admin vs admin users.
- Keep job definitions/state outside source control in writable runtime data.
- Maintain compatibility with OpenClaw job model and migration needs.

## Non-Goals (v1)

- Committing job payloads/schedules in repository config.
- Building dependency DAG orchestration between scheduled jobs.
- Replacing all existing built-in maintenance jobs in the same change set.

## Functional Requirements

### FR-1 Scheduler Job Domain Model

Add Core contracts for durable job definitions and state:

- `ScheduledJobDefinition`
  - identity: `Id`, `Name`, `Enabled`
  - schedule: `ScheduleKind` (`Cron`/`At`), `CronExpression` or `RunAtUtc`, `TimeZoneId`
  - execution: `ExecutionTimeoutSeconds`, `OverlapPolicy`
  - routing: `AgentId`, `SessionKey`, `SessionTarget`, `WakeMode`
  - delivery: `Channel`, `Recipient`, `Mode`
  - ownership/scope: `OwnerUserId`, `OwnerChannelId`, `Scope` (`Scoped`, `Global`)
  - payload: prompt/message body
- `ScheduledJobState`
  - `NextRunAtUtc`, `LastRunAtUtc`, `LastStatus`, `LastDurationMs`
  - `LastDeliveryStatus`, `LastError`, `LastErrorReason`
  - `ConsecutiveErrors`, `ConsecutiveSkips`

### FR-2 Scheduler Management Service

Introduce an explicit management interface (for tool and API usage), for example:

- create/get/list/update/delete
- enable/disable
- trigger-now
- list with ownership/scope filtering

Service is responsible for persistence, validation, and runtime scheduler synchronization.

### FR-3 Runtime Persistence

Persist jobs and state in writable runtime directory (e.g. `/app/data/scheduler`):

- `jobs.json` definitions
- `jobs-state.json` state
- schema versioning
- atomic writes
- restart-safe reload

### FR-4 Built-in Chat CRUD Tool

Add new `IOperationsTool` (e.g. `scheduled_jobs`) with explicit operation descriptors:

- `create_job`
- `get_job`
- `list_jobs`
- `update_job`
- `delete_job`
- `enable_job`
- `disable_job`
- `trigger_job`

Each operation must expose strict JSON schema aligned with management service contracts.

### FR-5 Chat Context-Based Defaults

Add per-turn execution context accessor so tools can infer:

- current sender/user id
- current channel id
- current session id

On `create_job`, default owner and target routing to current chat context unless explicitly overridden and authorized.

### FR-6 Authorization and Scope Rules

- Non-admin users:
  - can list/manage only own scoped jobs
  - cannot elevate to global scope unless policy allows explicit elevation path
- Admin users:
  - can manage all jobs, scoped and global

Map scheduler tool operations to explicit engagement action types (do not rely on unknown-tool allow behavior).

### FR-7 Scheduler Runtime Behavior

- timezone-aware cron support
- one-time `at` support
- overlap handling
- timeout handling
- explicit reason-coded failures

### FR-8 OpenClaw Compatibility and Migration

Keep compatibility with OpenClaw metadata model and provide import pathway:

- map OpenClaw fields to LeanKernel contracts
- dry-run validation for cron/timezone/channel/agent references
- preserve enabled/disabled status on import

Import is a compatibility feature, not the primary framing of this PRD.

### FR-9 Observability and Control Plane

Expose operational surfaces (API/UI and logs):

- job list + state
- recent run outcomes/errors
- manual trigger
- enable/disable and updates
- structured telemetry for run lifecycle and delivery outcome

## Architecture Mapping

- **LeanKernel.Core**: job/state/context/manager contracts.
- **LeanKernel.Scheduler**: management service, persistence adapter, runtime scheduling behavior.
- **LeanKernel.Plugins**: built-in `scheduled_jobs` operations tool.
- **LeanKernel.Thinker**: per-turn execution context population and tool authorization mapping.
- **LeanKernel.Commander**: delivery routing integration and outcome capture.
- **LeanKernel.Host**: DI wiring and optional admin API/UI management surfaces.

## Phased Delivery Plan

1. **Foundation contracts**
   - domain model + manager interfaces + context accessor contracts
2. **Management runtime**
   - persistent store + manager implementation + scheduler sync behavior
3. **Chat CRUD tool**
   - new `IOperationsTool` + schemas + service integration
4. **Scope and auth**
   - ownership filtering + admin behavior + action-type mapping
5. **Control plane + docs**
   - tests, API/UI exposure (if in-scope), and documentation updates

## Acceptance Criteria

- Agent can manage jobs from chat via built-in operations (create/get/list/update/delete/enable/disable/trigger).
- New jobs default to current session user + channel scope.
- Admins can manage all jobs; non-admins are scope-limited.
- Jobs/state persist across restart in runtime storage outside repository.
- Scheduler supports cron + one-time schedules with per-job timezone.
- Failures include explicit reason codes and are visible operationally.
- OpenClaw-style definitions can be mapped/imported without changing source-controlled job content.

## Risks and Dependencies

- No existing ambient tool execution context currently carries user/channel/session identity.
- Authorization mapping must be explicit for scheduler tool operations to avoid accidental allow paths.
- Timezone/DST correctness and timeout behavior require focused test coverage.
- Delivery prerequisites (e.g., channel conversation binding) must be validated pre-send and surfaced clearly.
