# Phase 21 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | GBrain `put_page` latency causes ingestion backlog under load | Documents take long to appear; durable queue depth grows | Configurable `MaxConcurrentJobs` bounds parallelism; retry/backoff policy protects downstream; monitor queue depth and worker lag | Open |
| R2 | FileSystemWatcher misses events (network share, rapid create/delete, Windows vs macOS differences) | Documents not ingested silently | Stability detection (settle delay + size check); periodic directory scan as safety net. Note: `FileSystemWatcher` on macOS uses `kqueue` (per-file, not per-directory); large directory trees may hit file descriptor limits. Linux vs macOS default buffer sizes and event coalescing behavior differ. Make `WatchSettleDelaySeconds` and the reconciliation scan interval configurable. | Open |
| R3 | Gateway cannot resolve scope for attachment event (e.g., invalid/anonymous credential) | Attachment cannot be ingested | Event emission is fail-closed; return 401 with clear message and preserve inline-only behavior for the current turn | Open |
| R4 | Document fingerprint collision across different tenants | Cross-tenant data leak | Fingerprint is scoped by availability-aware identity key (`tenant`/`user`/`channel` dimensions + fingerprint); tenant is part of the key, not just the fingerprint | Open |
| R5 | Policy misconfiguration grants unintended cross-channel document access | Data leak | Default is restrictive (no cross-channel access without explicit Share/Access config); audit logging via event spine | Open |
| R6 | Large documents (>10MB) consume excessive memory during extraction | OOM or slow pipeline | `FileSettings.MaxDownloadBytes` already caps download size; enforce same limit on ingestion; stage files once, then stream from disk during extraction | Open |
| R7 | Phase 21 scope creep into 5W1H fact extraction or knowledge UI | Phase stalls | Explicitly out-of-scope; document ingestion stores raw text only; fact extraction deferred to Phase 07 | Open |
| R8 | Durable job table growth under prolonged downstream failures | DB bloat and delayed ingestion | Retry caps, poison status, retention policy for completed/failed rows, and queue-depth alerting | Open |
| R9 | Worker crash leaves jobs stuck in `InProgress` lease | Jobs never complete | Lease expiry + startup stale-lease recovery that returns jobs to `Pending`; heartbeat/updated timestamps for active claims | Open |
| R10 | Explicit `channel_ids` parameter bypasses policy checks in document tools | Unauthorized data exposure | Enforce requested-channel subset check against resolver output; fail closed on any unauthorized channel id | Open |
| R11 | Asynchronous upload API returns completion fields before processing | Clients mis-handle queued jobs and present incorrect status | `POST /api/documents/upload` returns queue/job status only; optional status endpoint for completion state | Open |
| R12 | Availability scope misassignment (`tenant` vs `user` vs `channel`) causes over/under exposure | Data leak or missing expected results | Strict upload validation (user scope: always; channel scope: channel member; tenant scope: admin badge); safe defaults (`channel` for attachment events, `user` for uploads); integration tests per availability scope | Open |
| R13 | Non-admin caller uploads with `tenant` scope | Data leak | `POST /api/documents/upload` fails validation when caller badge is not admin and scope is `tenant` | Open |
| R14 | Upload API missing `channel_id` causes ambiguous channel scope | Ingestion fails or leaks | `channel_id` is a required parameter on upload endpoint; missing `channel_id` returns 400 validation error | Open |
| R15 | Document search scoped to current request's channel only (via `IChannelMemoryPolicyResolver`); user in Channel A cannot search documents from Channel B even if they have access to both separately | Cross-channel search blocked | Documented as a known limitation matching `GBrainMemoryClient` behavior (memory search has the same constraint). Future cross-channel search can be addressed by extending `IChannelMemoryPolicyResolver` to return aggregated readable sets across all channels the identity can access. | Open |

## Open Decisions
- D1: Channel attachment ingestion trigger source — terminal folder watcher vs flush-time event fan-out
  - **Decided**: Flush-time event fan-out from `IEventCollector.Emit<T>` path writes durable jobs; no event projector hosted service; terminal-side changes are backward-compatible.
- D2: Whether to store raw document content or rendered markdown in GBrain
  - **Proposed**: Store extracted text as-is (markdown for text files, plain text for PDF/DOCX). GBrain handles search indexing regardless of format.
- D3: Whether `document_search` should also search across 5W1H memory pages, or be document-only
  - **Proposed**: Document-only for v1. Cross-corpus search between documents and memory pages is a Phase 07 concern.
- D4: Availability scope default for uploads without explicit `availability_scope`
  - **Decided**: `channel` for channel attachment events; `user` for manual document-library uploads.
- D5: Queue durability model
  - **Decided**: DB-backed durable queue is the source of truth. Optional in-memory wake signal is allowed for responsiveness only.
- D6: Generic event envelope resolution in `DbEventStore`
  - **Decided**: Use `IHasEnvelope` marker interface instead of closed switch, so any event type is supported without modifying `DbEventStore`.
