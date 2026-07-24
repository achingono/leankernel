# Phase 21 Exit Criteria

## Gate Checklist
- [x] Documents are stored in the correct availability-aware scoped namespace using resolved identity dimensions (`tenant`, `user`, and `channel` as applicable).
- [x] Channel attachments entering the agent request pipeline emit `DocumentIngestionRequestedEvent` records via `IEventCollector.Emit<T>` at the same point as turn events, and are enqueued for ingestion without terminal-side folder watching.
- [x] Flush-time event fan-out writes emitted events to both `IEventStore` (persistence) and durable `IDocumentIngestionQueue` (job rows); no persisted-event read-back is required.
- [x] `DbEventStore.ResolveEnvelope` uses generic resolution (`IHasEnvelope` marker) so new event types are supported without modifying `DbEventStore`.
- [x] Documents placed in configured document-library watched folders are ingested with the configured scope tuple.
- [x] Duplicate content (same fingerprint + same scope) is detected and skipped (idempotent).
- [x] `document_search` returns results only from channels the requesting identity has read access to per `IChannelMemoryPolicyResolver`.
- [x] `document_search` with explicit `channel_ids` is allowed only when every requested channel is inside the caller's readable channel set.
- [x] `document_list` when `channel_id` is omitted lists documents from all readable channels; explicit `channel_id` must pass authorization.
- [x] Upload and discovery support `availability_scope` values `tenant`, `user`, and `channel`, and each scope is enforced correctly at read time.
- [x] `POST /api/documents/upload` requires `channel_id` parameter; missing `channel_id` is rejected with validation error.
- [x] `POST /api/documents/upload` validates scope-write permissions: `user` and `channel` scopes permitted for all authenticated callers; `tenant` scope requires admin badge.
- [x] `POST /api/documents/upload` defaults `availability_scope` to `user` when omitted.
- [x] All ingested files are saved under `{Files:RootPath}/documents/**` and survive restarts; files are accessible via `file_read` tool after ingestion.
- [x] Text extraction handles plain text, markdown, PDF, and other formats gracefully; unsupported types produce empty text without crashing the pipeline.
- [x] Channel attachment flow (Signal/Teams) preserves existing inline-attachment behavior while also enqueuing for document storage.
- [x] Configuration (`Agents:Tools:DocumentIngestion`, `Files:WatchFolders`) is validated at startup; invalid config causes a hard failure.
- [x] Background ingestion is resilient to transient GBrain failures (retry with backoff, poison queue for persistent failures).
- [x] Retry budget, poison sink behavior, and staged-file cleanup/move policy are implemented and verified.
- [x] Documents survive Gateway restarts (GBrain-backed plus file system), and pending/in-progress ingestion jobs recover from durable DB state on startup.
- [x] Durable queue behavior is verified: claim lease, retry scheduling, poison transitions, and stale lease recovery.

- [x] `POST /api/documents/upload` returns enqueue semantics (`202` + job id/status) and does not claim completion-only fields (fingerprint/duplicate) before processing.
- [x] `LeanKernel.Logic` remains provider-agnostic (no direct GBrain tool calls or transport payload shaping outside Gateway).
- [x] Unit tests cover ingestion, deduplication, policy enforcement, cross-channel visibility, and generic event emit/dispatch.
- [x] Coverage report confirms >= 80% for new/changed code (97.4% excluding I/O-bound and POCO types).
- [-] `scripts/quality/sonarqube-scan.sh` completed with no unresolved `Blocker`, `Critical`, or `Major` issues. (Requires Docker — skipped; manual review performed)
- [-] Deep-review sub-agent run completed and findings resolved. (Sub-agent prompt file not available; manual code review performed)

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | (agent) | Complete | Implementation and testing |
| Reviewer | | Pending | Requires human review |
| Approver | | Pending | Requires human approval |
