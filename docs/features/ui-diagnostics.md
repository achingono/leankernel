# Diagnostics UI

## Overview

The Diagnostics page is the operator-facing explorer for turn diagnostics. It lets a user load a persisted session id and inspect the latest available context audit, budget allocation, history shaping summary, routing decision, quality-gate outcome, and shadow-routing comparison for that session.

Unlike the chat page, Diagnostics is HTTP-backed. `DiagnosticsService` calls the Gateway diagnostics endpoints in parallel, merges the responses, and converts raw diagnostic entries into a single UI model.

## How It Works

### Session-based loading

- The page lives at `/diagnostics`.
- The operator enters a session id and selects `Load`.
- `DiagnosticsService.LoadAsync` requests these routes in parallel:
  - raw diagnostics feed
  - context diagnostics
  - budget diagnostics
  - history diagnostics
- The page shows warnings when some diagnostics are missing, but it still renders any data that was returned successfully.

The current UI is session-scoped, not turn-scoped. It resolves the latest available turn from the returned diagnostics data and uses that turn id when selecting routing, quality, and shadow payloads from the raw feed.

### Context audit visualization

The Context audit card splits diagnostics into `Admitted` and `Excluded` columns.

For each item, the page shows:

- the candidate key
- a normalized source category such as System, Wiki, History, Retrieval, or Tools
- the candidate score
- token count
- an admission or exclusion reason

When retrieval diagnostics are available, the footer also summarizes the effective scope, total candidates considered, admitted candidates, and exclusions caused by scope or score filtering.

### Budget usage

The budget card visualizes prompt assembly with:

- a stacked summary bar across System, Wiki, History, Retrieval, Tools, and reserved response headroom
- per-category bars showing used versus allocated tokens
- over-budget warnings when a category exceeds its allocation

Response headroom is rendered as reserved capacity derived from `TotalBudgetTokens - UsableBudgetTokens`.

### History shaping timeline

The history card shows:

- total verbatim, compacted, summarized, and dropped turns
- tokens saved for the selected turn
- a timeline of compaction markers when marker details were persisted

If marker details are not present, the UI falls back to count-based summary entries for compacted and summarized turns.

### Routing and quality panels

The routing card combines multiple diagnostics sources:

- `RoutingDecision` for selected model, tier, complexity, factors, and escalation source
- `QualityGateResult` for pass/fail status, overall score, failure reason, and per-check details
- `ShadowRoutingResult` for primary versus shadow model comparison, token counts, and deterministic comparison notes

This gives the operator one page that explains both why a model was selected and how its output was evaluated.

## Configuration

The page depends on shared diagnostics and gateway settings rather than UI-only settings.

- `LeanKernel:Diagnostics:*` controls whether the runtime persists the underlying context, budget, and history diagnostics.
- `LeanKernel:Gateway:ApiKey` or `LeanKernel:Gateway:ApiKeys` controls whether the diagnostics routes require `X-Api-Key`.
- `DiagnosticsService` automatically adds the configured Gateway API key to its internal `HttpClient` when a key is present.

If diagnostics persistence is disabled or no sink is configured, the page can still load, but cards may show empty states or warning banners.

## API Endpoints

The page consumes these Gateway routes:

| Endpoint | Method | Purpose |
| --- | --- | --- |
| `/api/diagnostics/{sessionId}` | `GET` | Load the raw diagnostics feed for the session. |
| `/api/diagnostics/{sessionId}/context` | `GET` | Load the persisted context admission audit. |
| `/api/diagnostics/{sessionId}/budget` | `GET` | Load budget allocation and token usage details. |
| `/api/diagnostics/{sessionId}/history` | `GET` | Load history-shaping counts and markers. |

When Gateway API-key auth is enabled, these routes require `X-Api-Key`. Missing diagnostics return warnings or `404`-style empty states instead of breaking the whole page.

## Screenshots / Examples

A typical operator view includes:

- a session-id input and `Load` button at the top
- a summary strip with session id, turn id, capture time, raw entry count, candidate count, and tokens saved
- side-by-side admitted and excluded context lists
- a stacked budget bar with category-by-category usage rows
- a history timeline with compaction and summary markers
- routing, quality, and shadow comparison panels in the lower half of the page

## Related documentation

- [Diagnostics](diagnostics.md)
- [Context Diagnostics API](context-diagnostics-api.md)
- [History Shaping](history-shaping.md)
- [Model Routing](model-routing.md)
- [Quality Gates](quality-gates.md)
- [Shadow Routing](shadow-routing.md)
