# PRD: Phase 4 Diagnostics Explorer UI

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Ship a Blazor Server diagnostics explorer that makes persisted per-turn context, budget, history-shaping, and routing decisions understandable from the Gateway UI.
- **Plan review:** Reviewed by `gpt-4.1`. Review outcome: proceed with the Gateway-local implementation, explicitly document accessibility and error handling, normalize partial endpoint failures into clear UI states, parse routing and quality-gate details from the raw diagnostics feed, and record the local `dotnet`/Sonar execution blocker during validation.

## Problem statement

LeanKernel already persists rich diagnostics and exposes them through authenticated API endpoints, but the Gateway diagnostics page is still a placeholder. Operators need a first-class UI that can load a session, inspect what context was admitted or excluded, see how token budgets were used, understand how history was compressed, and review the latest routing and quality-gate decisions without manually calling multiple endpoints.

## Scope

This task will:

1. Replace `src/LeanKernel.Gateway/Components/Pages/Diagnostics.razor` with an interactive Blazor Server diagnostics explorer page.
2. Add `src/LeanKernel.Gateway/Services/DiagnosticsService.cs` to fetch diagnostics API data via `HttpClient` and project the results into UI-friendly state.
3. Register the new service in `src/LeanKernel.Gateway/Program.cs` using the existing Gateway service-registration style.
4. Append diagnostics-specific BEM styles to `src/LeanKernel.Gateway/wwwroot/css/app.css` using the existing dark-first design tokens.
5. Surface loading, empty, partial-data, and error states for missing sessions, unauthorized responses, malformed payloads, and unavailable diagnostics categories.
6. Parse raw diagnostic entries from `GET /api/diagnostics/{sessionId}` to extract the latest `RoutingDecision`, `QualityGateResult`, and optional shadow-routing details for the currently loaded turn.
7. Record manual validation findings and the local `dotnet`/Sonar blocker if command execution is unavailable.

## Out of scope

- Changing the existing diagnostics API contracts or persistence behavior.
- Adding JavaScript charting libraries or client-side chart frameworks.
- Reworking the shared shell, navigation model, or unrelated Gateway pages.
- Adding automated tests or build tooling beyond what already exists in the repository.

## Functional requirements

### FR-1 Session load workflow

- The page exposes a session-id text input and a Load button.
- Submitting the form loads diagnostics for the requested session and shows a loading state while requests are in flight.
- Missing sessions return a clear, user-facing error banner.
- The page preserves the typed session id so operators can retry or correct it.

### FR-2 Context audit panel

- Render a two-column panel with admitted items on the left and excluded items on the right.
- Each item shows source key, category/source, token count, relevance score, and admission/exclusion reason.
- Admitted items are sorted by descending score.
- Context categories use visually distinct, accessible badges:
  - system = blue
  - wiki = purple
  - history = yellow
  - retrieval = green
  - tools = orange
- If no items exist for either side, show an empty-state message rather than leaving blank space.

### FR-3 Budget usage visualization

- Render category rows for System, Wiki, History, Retrieval, Tools, and Response Headroom.
- Show horizontal bars that compare used tokens against available budget/allocation using CSS only.
- Display both percentage utilization and absolute token counts.
- Over-budget categories are visually emphasized in error styling.

### FR-4 History shaping timeline

- Render a vertical timeline based on persisted history shaping markers when available.
- Each timeline item displays its order, applied strategy (`verbatim`, `compacted`, `summarized`, or `dropped`), and token-compression metadata.
- Use tier-specific colors: green = verbatim, yellow = compacted, orange = summarized, red = dropped.
- When marker data is unavailable, still show summary counters and the overall compression ratio.

### FR-5 Routing decision visibility

- Render a routing decision card showing the selected model, selected tier, complexity score, reason, and any factor list.
- Show alternatives considered using the best available data from escalation/shadow-routing diagnostics and label missing alternatives clearly.
- If a quality gate result exists, show the outcome, pass/fail state, overall score, failure reason, and individual checks when present.
- If routing diagnostics are absent, show a neutral empty state rather than an error.

### FR-6 Accessibility and resilience

- Use semantic headings, lists, forms, and sections.
- Add `aria-label`, `aria-live`, and `aria-busy` where they improve usability.
- Ensure text and badges maintain acceptable contrast against the dark-first palette.
- Partial endpoint failures should keep the rest of the page usable and show localized fallback messaging.

## Design notes

### Service design

- `DiagnosticsService` remains Gateway-local and contains no domain behavior beyond API retrieval and UI projection.
- The service uses `HttpClient` with the current app base address so the Blazor Server page can call the co-hosted API endpoints with relative URLs.
- To remain compatible with Gateway API-key validation, the service reads configured gateway API keys and sends the first available key as `X-Api-Key` when one is configured.
- The service fetches `/api/diagnostics/{sessionId}`, `/context`, `/budget`, and `/history`; typed endpoints are deserialized directly, while raw diagnostics entries are parsed from `JsonElement` payloads into `RoutingDecision`, `QualityGateResult`, and `ShadowRoutingResult` using the repository’s web-default JSON settings.
- Failures are normalized into a result object that distinguishes fatal load failures from partial section failures.

### Component design

- `Diagnostics.razor` uses `@rendermode InteractiveServer` and injects `DiagnosticsService` plus `NavigationManager` only if needed for future deep-link support.
- The page stores only view state: current session id, last loaded session id, loading flag, error text, and the latest diagnostics snapshot returned by the service.
- Helper methods inside the component derive:
  - admitted/excluded lists
  - category labels and CSS modifiers
  - formatted reason strings
  - budget utilization widths and over-budget state
  - history compression ratios and fallback timeline rows
  - routing alternative labels and quality-gate summaries

### Styling

- Append diagnostics-specific styles to `app.css` using `diagnostics-page*` BEM-style blocks.
- Reuse shared button, card, banner, badge, and page-header primitives where possible.
- Add responsive breakpoints so the audit columns collapse cleanly on narrower screens while preserving readability.

## Manual validation plan

1. Review the resulting diff for Gateway-only scope, naming consistency, and API-contract alignment.
2. Verify the diagnostics page route still resolves at `/diagnostics` and uses interactive server rendering.
3. Inspect the component/service flow to confirm all four endpoints are called and partial failures are handled.
4. Attempt repository validation commands if `dotnet` is available:
   - `dotnet restore src/LeanKernel.sln`
   - `dotnet build src/LeanKernel.sln --no-restore -v minimal`
   - `dotnet test src/LeanKernel.sln --no-build -v minimal`
   - `scripts/quality/test-coverage.sh`
   - `scripts/quality/sonarqube-scan.sh`
5. If `dotnet` is unavailable locally, record that blocker explicitly and rely on source-level validation plus diff review.

## Acceptance criteria

- `Diagnostics.razor` is a fully interactive diagnostics explorer instead of placeholder content.
- `DiagnosticsService` exists, is registered in DI, and calls the diagnostics APIs with `HttpClient`.
- The page shows session load, context audit, budget, history, and routing sections with graceful empty/error states.
- Diagnostics styles are appended to `app.css` and follow the existing design system.
- Validation evidence records the local environment blocker for `dotnet` and Sonar commands when they cannot be run.
