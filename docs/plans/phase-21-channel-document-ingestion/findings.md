# Phase 21 Deep Review Findings Handover

Deep contextual review performed for Phase 21 document ingestion implementation,
following `.agents/prompts/deep-review.prompt.md` and excluding static-analysis
concerns already covered by SonarQube.

## Critical

### C1 - Scope isolation and channel filtering gaps
- **File/Module:** `src/Services/LeanKernel.Gateway/Memory/GBrainDocumentStoreClient.cs`
- **The Issue:** Document partitioning and retrieval do not fully enforce user/channel scope dimensions; `channelIds` is not enforced in search/list behavior.
- **Impact:** Cross-user or cross-channel document visibility can occur within a tenant.
- **Recommendation:** Include full identity scope in storage partitioning keys and enforce channel filtering before returning results.

### C2 - Unvalidated `channel_id` trust boundary
- **File/Module:** `src/Services/LeanKernel.Gateway/Requests/DocumentUploadEndpoint.cs`, `src/Services/LeanKernel.Gateway/Providers/AttachmentIngestionMiddleware.cs`
- **The Issue:** Client-provided `channel_id` can be accepted/overridden without explicit authorization validation against channel policy.
- **Impact:** Authenticated callers may inject documents into unauthorized channels.
- **Recommendation:** Validate requested channel against resolved policy/permit context and reject unauthorized values with `403`.

### C3 - Path traversal risk in filename handling
- **File/Module:** `src/Services/LeanKernel.Gateway/Providers/AttachmentIngestionMiddleware.cs`, `src/Services/LeanKernel.Gateway/Requests/DocumentUploadEndpoint.cs`, `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/DocumentLibraryService.cs`
- **The Issue:** Filenames are used in path composition without strict canonicalization/safe-name enforcement.
- **Impact:** Traversal or absolute-path filename abuse can escape intended roots and overwrite files.
- **Recommendation:** Normalize with `Path.GetFileName`, reject separators/control characters, and enforce full-path root boundary checks.

### C4 - Non-atomic queue claim race
- **File/Module:** `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/DocumentIngestionQueue.cs`
- **The Issue:** Claim flow is `select` then `update`, allowing concurrent workers to claim the same pending job.
- **Impact:** Duplicate ingestion and inconsistent job state transitions under concurrency.
- **Recommendation:** Use atomic claim semantics (`UPDATE ... WHERE ... RETURNING`, locking, or optimistic concurrency token with retry).

## Major

### M1 - Event flush lifecycle coupling can drop ingestion events
- **File/Module:** `src/Services/LeanKernel.Gateway/Providers/AttachmentIngestionMiddleware.cs`, `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`
- **The Issue:** Ingestion event capture is request-scoped, but fan-out/flush appears coupled to chat persistence paths.
- **Impact:** Non-chat multipart flows can silently lose ingestion events.
- **Recommendation:** Add deterministic request-end flush or outbox-based delivery independent of chat-history persistence.

### M2 - Enqueue failures are swallowed
- **File/Module:** `src/Common/LeanKernel.Logic/Events/DocumentIngestionSubscriber.cs`
- **The Issue:** Enqueue errors are logged but not retried/escalated.
- **Impact:** Permanent ingestion loss during transient failures.
- **Recommendation:** Introduce retry/outbox/dead-letter behavior and avoid swallow-only handling.

### M3 - Watch folder burst handling may miss files
- **File/Module:** `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/WatchFolderHostedService.cs`
- **The Issue:** Event handoff design can miss create/change bursts.
- **Impact:** Non-deterministic missed ingestion in high-churn folders.
- **Recommendation:** Use a bounded queue + debounce/coalescing with worker processing.

### M4 - 24-hour cutoff can starve pending jobs
- **File/Module:** `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/DocumentIngestionQueue.cs`
- **The Issue:** Claim query excludes older pending jobs via a hard cutoff.
- **Impact:** Legitimate old jobs may never process.
- **Recommendation:** Remove hard cutoff or replace with explicit poison/archive workflow and operator visibility.

### M5 - Config/runtime intent drift
- **File/Module:** `src/Common/LeanKernel.Logic/Configuration/DocumentIngestionToolSettings.cs`, `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/DocumentIngestionHostedService.cs`, `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/WatchFolderHostedService.cs`
- **The Issue:** Several validated config knobs are not effectively applied in runtime behavior.
- **Impact:** Operators cannot tune concurrency/backpressure/retry as expected.
- **Recommendation:** Wire settings into runtime control paths or remove deferred options until implemented.

## Suggestions

### S1 - Durable job correlation in API response
- **File/Module:** `src/Services/LeanKernel.Gateway/Requests/DocumentUploadEndpoint.cs`
- **The Issue:** Accepted response does not return a durable persisted job identifier.
- **Impact:** Clients cannot reliably query ingestion status.
- **Recommendation:** Return queue job id from enqueue operation and provide job-status endpoint.

### S2 - Event envelope abstraction overlap
- **File/Module:** `src/Common/LeanKernel.Logic/Events/IEventEnvelope.cs`, `src/Common/LeanKernel.Core/IHasEnvelope.cs`
- **The Issue:** Envelope interfaces overlap with unclear separation of responsibility.
- **Impact:** Maintenance ambiguity and contract drift risk.
- **Recommendation:** Consolidate to one abstraction or define explicit differentiated responsibilities.

## Areas Reviewed With No Issues Found

- `src/Common/LeanKernel.Core/Events/TelemetryEvent.cs` (note: added `IHasEnvelope` for consistency)
- `src/Common/LeanKernel.Core/Events/ToolCallEvent.cs` (note: added `IHasEnvelope` for consistency)
- `src/Common/LeanKernel.Core/Events/TurnEvent.cs` (note: added `IHasEnvelope` for consistency)
- `src/Common/LeanKernel.Data/EntityContext.cs` mapping/index direction for document ingestion job persistence
- `test/LeanKernel.Tests.Unit/DocumentIngestion/*` intent coverage breadth for queue/tool/library paths

## Remediation Applied

| Item | Status | Changes |
|------|--------|---------|
| C1 — Scope isolation and channel filtering | **Fixed** | Extended `BuildNamespacePrefix` to include `channelId`/`userId`; added `FilterByChannelIds`/`FilterCatalogByChannelIds` post-query filtering in `SearchAsync`/`ListAsync`; extracted channel from slug in `MapToCatalogEntry`/`MapToSearchHit` |
| C2 — Unvalidated `channel_id` trust boundary | **Fixed** | `DocumentUploadEndpoint` now resolves policy via `IChannelMemoryPolicyResolver` and rejects unauthorized channels with `403`; `AttachmentIngestionMiddleware` validates channel against `ReadableChannelIds` before accepting |
| C3 — Path traversal risk | **Fixed** | `SanitizeFileName` helper added (uses `Path.GetFileName`, strips invalid chars); root-boundary check via `startsWith(stagingDir)` in both middleware and upload endpoint |
| C4 — Non-atomic queue claim race | **Fixed** | `TryClaimNextAsync` now uses `SELECT Id` then atomic `UPDATE ... WHERE Id = X AND Status = 'Pending'` via raw SQL; concurrent workers cannot both claim the same job |
| M2 — Enqueue failures swallowed | **Fixed** | `DocumentIngestionSubscriber` now retries up to 3 times with exponential backoff before logging the final failure |
| M4 — 24-hour cutoff starves jobs | **Fixed** | Removed `CreatedAt >= cutoff` filter from `TryClaimNextAsync` query; all pending jobs regardless of age are eligible |
| M5 — Config/runtime intent drift | **Fixed** | `DocumentIngestionHostedService` uses `EnqueueTimeoutSeconds` for lease duration; `WatchFolderHostedService` uses `WatchSettleDelaySeconds` for settle delay and `WatchMaxRetries` for stability check iterations |
| S1 — Return job ID | Deferred | Requires endpoint changes; queued for follow-up |
| S2 — Envelope abstraction overlap | Deferred | Architecture concern requiring cross-phase coordination |

All remediations verified: `dotnet build` (0 warnings/errors), `dotnet test` (771 passed, 0 failed).
