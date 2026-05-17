# Scheduled Jobs Management

LeanKernel now supports runtime-managed scheduled jobs through a built-in `scheduled_jobs` tool.

## Highlights

- Chat-driven operations: create, get, list, update, delete, enable, disable, and trigger.
- Scope defaults: jobs created from chat default to the current user/channel.
- Admin governance: admin actors can manage all jobs and create global jobs.
- Durable runtime storage under the writable data directory:
  - `scheduler/jobs.json`
  - `scheduler/jobs-state.json`
- Runtime execution includes:
  - cron and one-time (`at`) schedules
  - timezone-aware cron evaluation
  - timeout and overlap handling
  - execution/delivery status tracking

## Built-in Tool

- Tool name: `scheduled_jobs`
- Type: built-in `IOperationsTool`
- Operations:
  - `create_job`
  - `get_job`
  - `list_jobs`
  - `update_job`
  - `delete_job`
  - `enable_job`
  - `disable_job`
  - `trigger_job`

## Authorization Mapping

Scheduler operations are mapped to engagement action types:

- `ListScheduledJobs` for read/list operations
- `ManageScheduledJobs` for mutating operations

These action types are enforced through the existing tool execution authorizer pipeline.

## Context and Defaults

The scheduler tool reads ambient chat execution context (user/channel/session) populated during `ThinkerService.ProcessAsync`, and uses that context for ownership and delivery defaults unless explicitly overridden and authorized.
