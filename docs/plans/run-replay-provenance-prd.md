# PRD: Run Replay, Cost Timeline, and Context Provenance UI

## Overview

Add a run replay experience in the web UI that reconstructs agent execution step-by-step, including tool calls, token/cost timeline, and context provenance (what memory/doc/history was included and why).

## Problem Statement

Operators struggle to debug poor responses because they cannot easily see which context was used, where spend occurred, and how decisions evolved across tool calls.

## Goals

- Make every run explainable and replayable.
- Show token and cost evolution over time.
- Reveal context provenance and selection reasons.
- Reduce mean-time-to-diagnosis for failed or low-quality runs.

## Non-Goals (v1)

- Editable replay that re-executes from arbitrary checkpoints.
- Real-time collaborative annotation.
- Full distributed tracing backend replacement.

## User Stories

- As an operator, I can replay a run in chronological order.
- As a user, I can understand why a context item was included or excluded.
- As an admin, I can identify expensive steps and optimize routing.
- As support, I can export a run report for incident triage.

## Functional Requirements

### FR-1 Run Event Ledger

- Persist a normalized sequence of run events:
  - request start
  - context selection
  - model invocation
  - tool call start/finish
  - quality gate decisions
  - final response
- Include correlation IDs and parent-child links.

### FR-2 Replay Timeline UI

- Provide playback controls: play/pause, next/previous event, jump to event.
- Show event detail panel with payload summary.
- Highlight failures and retries.

### FR-3 Cost and Token Timeline

- Display per-step and cumulative token usage.
- Display per-step estimated cost and cumulative cost.
- Mark spikes and fallback-triggering steps.

### FR-4 Context Provenance View

- For each context item, show:
  - source type (history/wiki/document/tool output/system)
  - source ID/path
  - selection score and score factors
  - token contribution
  - included/excluded reason

### FR-5 Export and Share

- Export run diagnostics as JSON and markdown summary.
- Include privacy-safe redaction mode for sensitive fields.

### FR-6 Retention Controls

- Configurable retention windows for detailed traces vs summary data.
- Purge jobs for expired replay payloads.

## Non-Functional Requirements

- Replay page initial load <= 2s for runs with <= 500 events.
- Event fetch pagination for large runs.
- No blocking impact on live request latency > 5%.

## Architecture

| Component | Responsibility |
| --------- | -------------- |
| `RunEventStore` | Persist ordered event ledger |
| `CostAggregator` | Compute per-step and cumulative spend |
| `ProvenanceProjector` | Build context provenance records |
| `ReplayApi` | Serve replay and export endpoints |
| `ReplayUi` | Render timeline, details, and provenance panels |

## Data Model

### Run Event

```json
{
  "runId": "run_123",
  "eventId": "evt_010",
  "type": "tool_call_completed",
  "timestamp": "2026-05-07T12:01:15Z",
  "parentEventId": "evt_009",
  "payload": {
    "tool": "wiki_query",
    "latencyMs": 128,
    "status": "ok"
  }
}
```

### Provenance Item

```json
{
  "runId": "run_123",
  "itemId": "ctx_42",
  "sourceType": "wiki",
  "sourceRef": "data/wiki/what/project-atlas.md",
  "included": true,
  "selectionScore": 0.86,
  "scoreBreakdown": {
    "semantic": 0.39,
    "recency": 0.18,
    "dimension": 0.2,
    "frequency": 0.09
  },
  "tokenCount": 140
}
```

## API Requirements

| Method | Endpoint | Purpose |
| ------ | -------- | ------- |
| GET | `/api/runs/{runId}/replay` | Replay metadata and event stream |
| GET | `/api/runs/{runId}/cost` | Cost timeline and totals |
| GET | `/api/runs/{runId}/provenance` | Context provenance records |
| GET | `/api/runs/{runId}/export?format=` | Export run diagnostics |

## UI Requirements

- New route: `/runs/{id}` with tabs:
  - `Timeline`
  - `Cost`
  - `Context Provenance`
  - `Export`
- Chat page deep link from each answer to corresponding run replay.

## Security and Privacy

- Admin-only access in v1.
- Redact secrets and token-like values in payload previews.
- Respect data retention and purge policies.

## Telemetry and Success Metrics

- `run_replay_view_total`
- `run_export_total`
- `run_diagnosis_time_seconds` (time from first view to resolution marker)
- `debug_loop_reduction_percent` (pre/post)

## Rollout Plan

1. Phase 0: internal event schema and storage.
2. Phase 1: timeline and cost tab.
3. Phase 2: provenance tab and scoring breakdown.
4. Phase 3: export/reporting and retention controls.

## Acceptance Criteria

- AC-1: Any completed run can be replayed end-to-end.
- AC-2: Cost timeline equals backend token accounting within 1%.
- AC-3: Provenance list includes include/exclude reason for all candidate context items.
- AC-4: Exports produce deterministic output for same run ID and redaction mode.
- AC-5: Replay UI remains responsive for 500-event runs.

## Dependencies

- Structured function logging middleware.
- Context gatekeeper scoring internals exposed as structured metadata.
- Token usage and model metadata from LiteLLM responses.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| High storage growth | Tiered retention and event payload compaction |
| Sensitive data leakage | Default redaction and field allowlist |
| Event ordering drift | Monotonic sequence IDs and server timestamping |

## Open Questions

- Should non-admin users get scoped replay access in v2?
- Which export format is primary for support workflows (JSON or markdown)?
- Should replay include raw prompts by default or only summarized snippets?

## Implementation Clarifications (v1 Defaults)

Use these defaults unless support or privacy review overrides them during grooming:

- The run event ledger is append-only and uses a server-assigned monotonic sequence number in addition to timestamps so replay ordering is deterministic.
- Provenance capture includes every candidate context item considered by the selector, not just included items, with summarized evidence and score factors.
- Raw prompts and tool payloads are redacted by default in UI and markdown export. JSON export is the source-of-truth artifact for admins and still applies secret masking.
- Detailed replay payloads are retained for 7 days and summary records for 30 days unless an incident hold is applied.
- v1 access remains admin-only. Scoped user access is explicitly deferred.

## Sprint-Ready Engineering Tickets

- [ ] `RRP-01` Define the normalized run event, cost snapshot, and provenance item contracts, including sequence IDs, parent-child links, and redaction markers.
- [ ] `RRP-02` Instrument the request pipeline to emit request start, context selection, model invocation, tool call, quality gate, and final response events with correlation IDs.
- [ ] `RRP-03` Implement backend aggregation for per-step and cumulative token and cost accounting, plus reconciliation tests against LiteLLM usage metadata.
- [ ] `RRP-04` Build replay, cost, provenance, and export endpoints with pagination, retention-aware reads, and privacy-safe redaction modes.
- [ ] `RRP-05` Add the `/runs/{id}` UI route with timeline playback controls, event detail panes, failure highlighting, and a cost tab that exposes spikes and fallback steps.
- [ ] `RRP-06` Implement the context provenance tab and export flows so operators can inspect include/exclude reasons, score breakdowns, source references, and token contribution.
- [ ] `RRP-07` Add retention jobs and integration/load tests covering 500-event runs, deterministic exports, and replay responsiveness before the feature is exposed outside internal ops.
