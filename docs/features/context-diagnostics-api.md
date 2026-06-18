# Context Diagnostics API

> Canonical API reference now lives at [`../api/diagnostics-api.md`](../api/diagnostics-api.md). This page is retained for compatibility links during docs migration.

Phase 2 adds a dedicated context-diagnostics surface so operators can inspect the exact turn-time context decisions LeanKernel persisted during prompt assembly. The API does not recompute those decisions from mutable state; it reads stored per-turn snapshots captured by `TurnPipeline`.

## Why this feature matters

Context-heavy systems are easier to trust when operators can answer questions such as:

- why was a retrieval candidate excluded?
- how much history survived shaping?
- which part of the prompt budget was consumed?

The context-diagnostics API answers those questions from stored snapshots instead of inferring them later.

## Implemented surface

The current code exposes four related observability layers.

| Surface | Current implementation |
| --- | --- |
| Generic diagnostics endpoint | `GET /api/diagnostics/{sessionId}` in `LeanKernel.Gateway` |
| Context diagnostics endpoints | `GET /api/diagnostics/{sessionId}/context|budget|history` |
| Turn-assembly models | `ConversationContext.AdmissionLog`, `BudgetUsage`, `RetrievalDiagnostics`, and `HistoryDiagnostics` |
| Diagnostics infrastructure | `ContextDiagnosticsService`, `DiagnosticsCollector`, and `IDiagnosticsSink` |

`TurnPipeline` creates a stable `turnId` before context assembly, stores it in message metadata, and writes one `ContextSnapshot` diagnostic entry after context assembly and tool visibility resolution.

## Gateway endpoints

Gateway exposes these diagnostics routes:

| Endpoint | Method | Purpose |
| --- | --- | --- |
| `/api/diagnostics/{sessionId}` | `GET` | Returns persisted `DiagnosticEntry` items for a session. |
| `/api/diagnostics/{sessionId}/context` | `GET` | Returns the stored context admission audit for the latest turn or a specific `turnId`. |
| `/api/diagnostics/{sessionId}/budget` | `GET` | Returns stored budget allocation and usage details for the latest turn or a specific `turnId`. |
| `/api/diagnostics/{sessionId}/history` | `GET` | Returns stored history shaping diagnostics for the latest turn or a specific `turnId`. |

Like other protected Gateway endpoints, they use the same optional `X-Api-Key` mechanism.

```bash
curl http://localhost:5080/api/diagnostics/session-123/context \
  -H "X-Api-Key: replace-me"
```

The `/context`, `/budget`, and `/history` routes accept an optional `?turnId=` query parameter. When no matching snapshot exists, they return `404` with a structured `{ "error": "..." }` payload.

## Stored snapshot contents

Each persisted context snapshot includes:

- `Admissions`: the admitted and excluded context candidates with token counts and exclusion reasons
- `BudgetUsage`: actual token usage for system prompt, wiki facts, retrieval, conversation history, and tools
- `Budget`: the configured usable budget slices for the turn
- `TotalBudgetTokens` and `ResponseHeadroomRatio`: the turn-time totals used to derive usable budget
- `RetrievalDiagnostics`: scoped-retrieval decisions when retrieval diagnostics were available
- `HistoryDiagnostics`: history shaping counts and token totals when shaping diagnostics were available
- `Timestamp`: when the snapshot was stored

## Response shapes

### `/api/diagnostics/{sessionId}/context`

Returns:

- `SessionId`
- `TurnId`
- `Timestamp`
- `Admissions`
- `TotalCandidatesConsidered`
- `TotalAdmitted`
- `TotalExcluded`
- optional `RetrievalDiagnostics`

### `/api/diagnostics/{sessionId}/budget`

Returns:

- `TotalBudgetTokens`
- `UsableBudgetTokens`
- `ResponseHeadroomRatio`
- `Usage`
- per-category `BudgetCategoryDetail` entries for system prompt, wiki facts, retrieval, conversation, and tools

### `/api/diagnostics/{sessionId}/history`

Returns:

- `SessionId`
- `TurnId`
- optional `Shaping`
- `VerbatimTurns`
- `CompactedTurns`
- `SummarizedTurns`
- `DroppedTurns`
- `TokensSaved`

## Runtime flow

```mermaid
flowchart LR
    M[LeanKernelMessage + turnId] --> T[TurnPipeline]
    T --> C[ContextGatekeeper]
    C --> X[ConversationContext diagnostics]
    T --> S[ContextDiagnosticsService]
    S --> D[IDiagnosticsSink / DiagnosticEntry ContextSnapshot]
    D --> G[/api/diagnostics/{sessionId}/context|budget|history]
```

## Configuration

Context diagnostics are controlled under `LeanKernel:Diagnostics` plus Gateway API-key auth.

| Key | Default | Purpose |
| --- | --- | --- |
| `LeanKernel:Diagnostics:Enabled` | `true` | Enables diagnostics collection paths. |
| `LeanKernel:Diagnostics:PersistToDatabase` | `true` | Persists diagnostic entries through the configured sink. |
| `LeanKernel:Diagnostics:ContextDiagnosticsEnabled` | `true` | Enables persisted context snapshot writes and API reads. |
| `LeanKernel:Diagnostics:MaxDiagnosticsPerSession` | `100` | Caps how many stored context snapshots are considered when resolving a session query. |
| `LeanKernel:Diagnostics:ServiceName` | `leankernel` | Service name used in diagnostics and log enrichment. |
| `LeanKernel:Gateway:ApiKey` | empty | Protects the diagnostics endpoints when configured. |
| `LeanKernel:Gateway:ApiKeys` | empty | Optional multi-key override form. |

## How to think about the current state

The best way to think about context diagnostics now is:

- the runtime persists exact context-assembly evidence per turn
- the generic diagnostics endpoint still returns raw `DiagnosticEntry` rows
- the dedicated Phase 2 endpoints project stored snapshots into context, budget, and history views
- malformed legacy snapshot payloads are skipped rather than failing the API

## Related documentation

- [Diagnostics](diagnostics.md)
- [Context Gating](context-gating.md)
- [Scoped Retrieval](scoped-retrieval.md)
- [History Shaping](history-shaping.md)
- [Gateway API](gateway-api.md)
- [Phase 2 Configuration](../configuration/phase-2-config.md)
