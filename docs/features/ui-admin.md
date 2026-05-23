# Admin UI

## Overview

The Admin page is LeanKernel's governance console preview. It brings provider health, routing tables, tool governance, spend summaries, and scheduled-job status into a single Blazor page, but the current implementation is explicitly mock-backed and non-persistent.

That preview status is part of the feature contract today. The page is useful for validating layout, interaction patterns, and operational summaries, but it does not yet write real runtime configuration through dedicated admin APIs.

## How It Works

### Mock-backed dashboard loading

- The page lives at `/admin`.
- `Admin.razor` loads its data from scoped `AdminService`.
- `AdminService` builds an `AdminDashboardSnapshot` containing provider health, routing rules, tool rows, spend data, and scheduled jobs.
- The page shows both `Mock-backed preview` and `Non-persistent` status badges so operators know the current limits.

### Provider health monitoring

The `System health overview` section renders one card per provider with:

- provider name and summary lane
- health status badge
- latency in milliseconds
- last check time

`Refresh checks` calls `AdminService.RefreshProviderHealthAsync`, which regenerates the mock provider snapshot in memory for the current Blazor circuit.

### Model routing configuration table

The routing table is read-only in the current preview. It lists tier, primary model, fallback model, max tokens, and cost per 1k tokens for the mock routing rules returned by `AdminService`.

### Tool governance management

The tool governance table supports:

- category filtering
- enabled/disabled toggles
- visibility scope display
- expandable tool descriptions

Toggles call `AdminService.SetToolEnabledAsync`, which updates the in-memory mock list only. The page footnote explicitly states that these changes are stored in preview state only.

### Spend tracking dashboard

The spend section combines:

- summary cards for today, this week, this month, and budget status
- a seven-day vertical bar chart
- a monthly budget meter when a mock budget limit is present

These values come from mock dashboard data and are intended to preview the operator experience rather than report live spend totals.

### Scheduled jobs overview

The jobs table lists the current mock cadence for scheduled work, including:

- job name
- cron schedule string
- last run time
- next run time
- status badge

This mirrors the shape of the scheduler information the full admin surface is expected to expose later, without claiming that real job control is implemented now.

## Configuration

The Admin page has no dedicated configuration block and does not currently persist its changes.

Its behavior depends on the Blazor page and `AdminService` only:

- provider health refresh regenerates mock data in memory
- tool toggles mutate mock state in the scoped service
- routing, spend, and jobs are read from preview models

Because the page is a preview surface, changing runtime configuration files does not automatically update the rows shown here unless `AdminService` is updated to read them.

## API Endpoints

The current Admin page does not consume dedicated Gateway admin endpoints. All data is supplied by the in-process `AdminService` used by the Blazor circuit.

## Screenshots / Examples

A user opening `/admin` sees:

- a page header with preview badges and the last refresh time
- a grid of provider health cards with latency and status badges
- a read-only routing table
- a filterable tool-governance table with enable/disable toggles
- spend summary cards, a seven-day bar chart, and a monthly budget meter
- a scheduled-jobs table with cron expressions and status chips

## Related documentation

- [Model Routing](model-routing.md)
- [Tool Governance](tool-governance.md)
- [Production Operations](production-ops.md)
- [Scheduler](scheduler.md)
