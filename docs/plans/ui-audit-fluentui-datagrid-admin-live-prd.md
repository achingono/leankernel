# PRD: UI Audit — FluentDataGrid Admin Live Data + Playwright Coverage

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and contributors implementing the next Gateway UI hardening slice
- **Task goal:** Replace Admin preview tables with FluentDataGrid, wire Admin data to real runtime sources where practical, and expand Playwright coverage across Gateway pages
- **Plan review:** Reviewed by **GPT-5.4**. Review outcome: **proceed with caveats** around FluentDataGrid API details, scheduler read-model limits, routing-config shape mismatch, and Playwright selector stability.

## Problem statement

`src/LeanKernel.Gateway/Components/Pages/Admin.razor` already uses Fluent UI cards, labels, badges, switches, and loading states, but its routing, tool-governance, and scheduled-job sections still render raw HTML `<table>` markup. At the same time, `src/LeanKernel.Gateway/Services/AdminService.cs` is fully mock-backed with hardcoded data and `Task.Delay` placeholders, so the Admin page does not yet reflect live runtime state for tools, health, or scheduler-backed operations.

The next slice should make the Admin surface feel like a real operator console: use `FluentDataGrid<T>` for tabular sections, pull live data where the runtime already exposes it, clearly label mock/config/live sections, and add stronger UI test coverage for the Gateway pages.

## Current-state findings

- LeanKernel is a modular monolith on **.NET 10**.
- `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj` already references `Microsoft.FluentUI.AspNetCore.Components` and `.Icons` at **v4.14.2**.
- `Admin.razor` currently has three raw HTML tables:
  - routing rules
  - tool governance
  - scheduled jobs
- `AdminService` is currently mock-backed for provider health, routing rules, tools, spend, and scheduled jobs.
- Gateway already maps both:
  - `/healthz` via `MapHealthChecks(...)`
  - `/api/health` via `HandleHealthAsync(...)`, which returns structured JSON including provider health
- `IToolRegistry` exists and exposes `GetVisibleTools(ToolVisibilityContext context)`.
- `ToolVisibilityContext` is constructible for admin use, but `ToolDefinition` does **not** include persisted enabled-state or visibility-scope metadata.
- `SchedulerHostedService` exposes scheduler activity such as `InFlightCount`, but does **not** currently expose a per-job UI read model for live status.
- `test/LeanKernel.Tests.Playwright/` exists, but only contains `NavigationTests.cs` with basic page-load assertions.

## Goals

1. Replace authored raw `<table>` markup in Admin with `FluentDataGrid<T>`.
2. Wire AdminService to live runtime data where the platform already has trustworthy sources.
3. Keep mock-only sections explicit instead of presenting them as live.
4. Standardize section layout and loading behavior across the Admin page.
5. Add page-specific Playwright coverage for Admin, Chat, Diagnostics, Knowledge, and Onboarding.

## Non-goals

- Building a full admin governance API in this slice.
- Persisting tool enable/disable changes to a backend policy store.
- Implementing real spend tracking before runtime spend telemetry exists.
- Adding new scheduler infrastructure solely to satisfy the UI, unless a small read-model abstraction is needed.
- Reworking unrelated Gateway page behavior beyond what is required for stable UI tests.

## Scope

### 1. Replace raw HTML tables with FluentDataGrid in `Admin.razor`

Rewrite the three tabular Admin sections to use `FluentDataGrid<T>`:

1. **Routing rules**
2. **Tool governance**
3. **Scheduled jobs**

Expected outcomes:

- built-in sorting where the component/library supports it cleanly in v4.14.2
- consistent Fluent UI styling
- improved accessibility and keyboard navigation
- template-based cells for badges, switches, helper text, and formatted values

Implementation expectations:

- `Admin.razor` source should no longer author raw `<table>` blocks for these sections.
- Grid columns should prefer `PropertyColumn` where simple binding is enough and `TemplateColumn` where badges, toggles, or multi-line helper content are required.
- The tool-governance grid should retain category filtering.
- The scheduled-jobs grid should preserve status badges.
- The routing grid should preserve numeric formatting for tokens and cost values.

### 2. Wire `AdminService` to real runtime services

#### 2.1 Tool governance

Use `IToolRegistry` as the live source for the tool list.

Requirements:

- Build tool rows from real `ToolDefinition` instances.
- Construct a safe admin `ToolVisibilityContext`; an empty/default context is acceptable unless governance policy requires more explicit values.
- Keep enabled/disabled toggle state **local to `AdminService`** until a real governance API exists.
- Because `ToolDefinition` does not currently expose persisted enabled-state or visibility-scope fields, document that:
  - enablement is preview/local state
  - visibility scope may remain derived/synthetic until a richer governance model exists

#### 2.2 Health data

Use real ASP.NET Core health data for provider/system health.

Preferred source order:

1. inject and use `HealthCheckService` / existing provider-health services directly in-process, or
2. reuse the existing structured `/api/health` contract if a UI-facing HTTP call is preferred

Avoid parsing `/healthz` response text for UI state if a structured source is available.

Requirements:

- replace mock provider health rows with live provider/system health data
- preserve refresh behavior
- add a **Live** badge/indicator for sections using real runtime data

#### 2.3 Scheduled jobs

Wire scheduled-job data to the scheduler configuration and persisted execution history as far as current runtime support allows.

Requirements:

- source job definitions from scheduler config/database-backed history where available
- use `SchedulerHostedService` only for state it actually exposes
- do **not** claim per-job live status if the hosted service does not provide it

Planned fallback:

- if no per-job live status read model exists, show:
  - configured job name/schedule
  - latest known execution timestamps/outcome from persistence
  - a section badge such as **Config-backed** or **Partially live**

#### 2.4 Routing rules

Attempt to source routing rows from `LeanKernelConfig.Routing`.

Important constraint:

- current routing config shape appears closer to `Economy`, `Standard`, and `Premium` tiers than the existing Admin table's `Low`, `Medium`, `High`, and `Critical` rows

Plan:

- if a clean mapping exists, render routing rows from config
- otherwise keep this section hardcoded for now and label it **Config gap** or equivalent in the PRD/implementation notes

#### 2.5 Spend dashboard

Keep spend data mock-backed for this slice.

Requirements:

- retain current spend cards/chart behavior
- label spend explicitly as **Mock** or **Preview**
- do not imply budget enforcement or live cost telemetry exists

#### 2.6 Status labeling

Remove the page-level **Mock-backed preview** badge once the page becomes mixed-source.

Replace it with per-section status indicators such as:

- **Live**
- **Config-backed**
- **Mock**
- **Partially live**

### 3. UI/UX consistency

Apply the following page-wide consistency rules:

- all tabular data uses `FluentDataGrid`
- every section is wrapped in `FluentCard`
- section headings follow:
  - `FluentLabel` with `Typography.PageTitle` or `Typography.Subject` for title
  - `FluentLabel` with neutral body text for description
- loading states use `FluentProgressRing` plus helper text
- badges and toggles use Fluent UI components consistently

Accessibility expectations:

- keep explicit helper text for loading and refresh actions
- preserve non-color indicators for status
- use stable accessible names for refresh controls, toggles, filters, and section labels

## Playwright test expansion

Add focused page test files under `test/LeanKernel.Tests.Playwright/`:

### `AdminPageTests.cs`

Cover:

- health cards load
- FluentDataGrid sections render with rows
- tool toggle interaction works
- refresh button triggers a reload observable in the UI

### `ChatPageTests.cs`

Cover:

- composer input exists
- send button is disabled when empty
- new session button works

### `DiagnosticsPageTests.cs`

Cover:

- session ID input renders
- load button enabled/disabled states behave correctly

### `KnowledgePageTests.cs`

Cover:

- search input renders
- browse list loads
- page detail panel renders

### `OnboardingPageTests.cs`

Cover:

- wizard steps render
- required form inputs are present
- navigation between steps works

Test design requirements:

- prefer stable selectors such as ids, roles, labels, or deliberate test hooks over brittle content-only assertions
- use explicit waits for interactive Blazor state transitions
- keep test data deterministic where possible to reduce flake
- avoid assertions that depend on truly external runtime state unless the test fixture seeds or controls that state

## Files in scope

- `src/LeanKernel.Gateway/Components/Pages/Admin.razor`
- `src/LeanKernel.Gateway/Services/AdminService.cs`
- `src/LeanKernel.Gateway/Program.cs` and/or `src/LeanKernel.Gateway/Endpoints.cs` if needed for health-data reuse
- any small supporting DTO/read-model files required to keep AdminService cohesive
- `test/LeanKernel.Tests.Playwright/AdminPageTests.cs`
- `test/LeanKernel.Tests.Playwright/ChatPageTests.cs`
- `test/LeanKernel.Tests.Playwright/DiagnosticsPageTests.cs`
- `test/LeanKernel.Tests.Playwright/KnowledgePageTests.cs`
- `test/LeanKernel.Tests.Playwright/OnboardingPageTests.cs`

## Validation criteria

Implementation will be considered complete when all of the following are true:

1. `dotnet build src/LeanKernel.sln --no-restore -v minimal` passes.
2. `dotnet test src/LeanKernel.sln --no-build -v minimal` passes.
3. `Admin.razor` uses `FluentDataGrid` for routing rules, tool governance, and scheduled jobs, with no authored raw HTML `<table>` markup remaining for those sections.
4. Admin shows the real tool list from `IToolRegistry`.
5. Live/config/mock section indicators accurately reflect the underlying data source.
6. Playwright test files compile and define the expected page-specific test methods.

## Risks and open questions

1. **FluentDataGrid API compatibility with v4.14.2**
   - Risk: exact markup/API shape for sortable columns and template columns may differ from assumptions.
   - Mitigation: do a quick API spike before editing all three grids.

2. **Tool visibility context**
   - Risk: governance rules may expect a more explicit `ToolVisibilityContext` than an empty/default context.
   - Mitigation: document the chosen admin context and keep it centralized in `AdminService`.

3. **Scheduler read-model gap**
   - Risk: `SchedulerHostedService` may not expose enough per-job state for a truly live jobs table.
   - Mitigation: use config + persisted execution history first; add a small read model only if necessary.

4. **Routing model mismatch**
   - Risk: `LeanKernelConfig.Routing` does not obviously match the current four-row Admin table.
   - Mitigation: define the mapping explicitly or keep the section hardcoded and clearly labeled until config evolves.

5. **Playwright regressions**
   - Risk: replacing page structure and adding richer assertions may break the current lightweight navigation tests or introduce flake.
   - Mitigation: preserve stable page content, add resilient selectors, and use deterministic waits/assertions.

## Reviewed Notes

This PRD was reviewed by **GPT-5.4** before implementation. The review flagged the following concerns and additions:

- **FluentDataGrid v4.14.2:** proceed, but verify the exact `Items`, sortable column, `PropertyColumn`, and `TemplateColumn` APIs before rewriting all three Admin tables.
- **`IToolRegistry.GetVisibleTools(...)`:** the call itself is not inherently hard to use from Admin because `ToolVisibilityContext` is easy to construct, but the returned `ToolDefinition` model lacks persisted enablement and visibility metadata. The PRD therefore keeps toggle state local and treats some visibility fields as synthetic until a governance API exists.
- **`SchedulerHostedService`:** current scheduler APIs do not obviously expose per-job UI status. The jobs section should therefore use real config/history where possible and avoid overstating “live” status unless a read model is introduced.
- **Existing Playwright tests:** compile risk is low, but flake risk is moderate once tests assert interactive UI state. The implementation should prefer stable ids/roles/labels, deterministic data, and observable refresh outcomes.
- **Health source preference:** prefer in-process health services or the existing structured `/api/health` payload over scraping `/healthz`.

## Acceptance summary

This slice should leave the Gateway Admin page looking and behaving like a real operations surface: Fluent-native grids instead of handcrafted tables, real runtime data where LeanKernel already exposes it, explicit labeling where data is still mock or config-backed, and materially better UI coverage in Playwright.
