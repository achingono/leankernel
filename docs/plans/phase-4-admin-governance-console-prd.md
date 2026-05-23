# PRD: Phase 4 Admin Governance Console

## Overview

Build the first real `/admin` experience in `LeanKernel.Gateway` as a Blazor Server runtime-governance dashboard. The page should replace the existing placeholder with a dark-first, data-dense admin surface that lets operators inspect provider health, review model routing, govern tools, monitor spend, and see scheduled job status.

This slice is intentionally mock-backed because the Gateway admin endpoints are not fully implemented yet. The UI and service contract should still be shaped so real endpoint wiring can replace the mock internals later without reworking the page structure.

## Current-State Findings

- `src/LeanKernel.Gateway/Components/Pages/Admin.razor` is a placeholder card only.
- `Chat.razor` establishes the current Blazor Server pattern: `@rendermode InteractiveServer`, scoped service injection, async initialization, and minimal component-local state.
- `src/LeanKernel.Gateway/wwwroot/css/app.css` already provides the dark-first design tokens, shared shell styles, button styles, and reduced-motion handling.
- Gateway currently registers `ChatService` but has no admin-specific UI service.
- The user explicitly requested that build/test execution be skipped because `dotnet` is not available in the environment.

## Goals

- Replace the placeholder admin page with a polished, information-dense governance dashboard.
- Use semantic, accessible tables and cards for all operational data.
- Provide loading skeletons so the page feels intentional even while mock data loads.
- Keep interactive behavior lightweight and clearly labeled as preview/mock where persistence is not yet available.
- Define a service contract that can evolve from in-memory mock data to real admin APIs.

## Non-Goals

- Persisting admin changes to the backend in this slice.
- Adding new backend admin endpoints.
- Implementing inline editing for routing rules beyond display-ready read-only cells.
- Adding third-party chart libraries or JavaScript-heavy data visualization.

## UI Scope

### 1. System Health Overview

- Top section with provider health cards in a responsive grid.
- Each card shows provider name, colored health indicator, status text, latency, and last check time.
- Include a refresh action that re-runs mock health data generation.
- Use `aria-busy` while refresh is in progress.

### 2. Model Routing Configuration

- Render a semantic table with caption.
- Show rows for `low`, `medium`, `high`, and `critical` routing tiers.
- Columns: tier, model, fallback model, max tokens, and cost-per-1k.
- Model/fallback values should look editable in the future but remain read-only in this phase.

### 3. Tool Governance

- Render a semantic table with caption.
- Include a category filter above the table.
- Columns: tool name, category, enabled toggle, visibility scope, and description affordance.
- Do not rely on hover alone for descriptions; add an accessible expand/details interaction that also works for keyboard and touch.
- Toggle interactions update mock service state only and are labeled as preview behavior.

### 4. Spend Tracking Dashboard

- Summary cards for today, this week, and this month.
- CSS-only 7-day bar chart with explicit value labels so meaning is not color-only.
- Budget indicator card or meter when a monthly budget is configured; otherwise show an unconfigured state.

### 5. Scheduled Jobs

- Render a semantic table with caption.
- Columns: job name, cron schedule, last run, next run, and status.
- Status badges: running, idle, failed.

## Service Contract Plan

Create `src/LeanKernel.Gateway/Services/AdminService.cs` as a scoped service with async methods such as:

- `GetDashboardAsync(CancellationToken)`
- `RefreshProviderHealthAsync(CancellationToken)`
- `SetToolEnabledAsync(string toolName, bool enabled, CancellationToken)`

The service should expose admin-focused DTOs/records for:

- provider health entries
- routing rules
- tool governance entries
- spend summaries and daily spend points
- budget status
- scheduled jobs
- page-level dashboard snapshot

Implementation notes:

- Use in-memory mock state so refreshes and toggles visibly update during the active Blazor session.
- Keep HTTP/backend details out of the component so the future implementation can replace service internals cleanly.
- Prefer optional fields where the eventual backend may not yet provide exact latency, budget, or schedule timing values.

## Component Plan

Rewrite `src/LeanKernel.Gateway/Components/Pages/Admin.razor` to:

- inject `AdminService`
- set `<PageTitle>LeanKernel Admin</PageTitle>`
- use `@rendermode InteractiveServer`
- load dashboard data in `OnInitializedAsync`
- show skeleton states before first load
- support provider health refresh and tool-category filtering
- support accessible tool-description disclosure and mock enabled toggles
- expose helper formatting methods for timestamps, money, and percentages
- show a lightweight status banner or helper copy indicating preview/mock data where appropriate

## Styling Plan

Append admin-specific BEM-style CSS to `src/LeanKernel.Gateway/wwwroot/css/app.css` using selectors such as:

- `admin-page`
- `admin-page__section`
- `admin-health-grid`
- `admin-health-card`
- `admin-table`
- `admin-toggle`
- `admin-spend-chart`
- `admin-status-badge`
- `admin-skeleton`

Styling should:

- reuse existing color tokens, radii, and shadows
- preserve dark-first polish while honoring the existing light-mode token overrides
- provide visible focus states
- include responsive adjustments for tablet/mobile widths
- respect reduced-motion behavior already defined globally

## Accessibility Requirements

- Use semantic `table`, `caption`, `thead`, `tbody`, `th`, and `td` markup.
- Use row headers where helpful for scanability.
- Add `aria-label` to refresh, filter, toggle, and disclosure controls.
- Do not communicate state by color alone; pair colored dots/badges with text labels.
- Make tool descriptions accessible without hover.
- Keep touch targets comfortable and visually distinct.

## Review Notes Incorporated

This plan was reviewed with a different model before implementation. The review surfaced and this plan incorporates the following changes:

1. The reviewed plan must be saved under `docs/plans` before implementation begins.
2. The page should include explicit `PageTitle`, loading semantics (`aria-busy`), and clear preview/non-persistent labeling for mock interactions.
3. Tool descriptions cannot rely on hover alone; they need an accessible disclosure pattern.
4. The service contract should stay UI-focused rather than assuming immediate HTTP endpoint shapes.

## Validation Plan

- Perform targeted code inspection and diff review after implementation.
- Attempt repository validation commands only if the required tooling exists.
- Because the user stated `dotnet` is unavailable in this environment, document that build/test execution is skipped rather than claiming it ran.
- Use static review and diagnostics available in the workspace to reduce the risk of syntax or integration mistakes.
