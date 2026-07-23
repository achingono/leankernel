# Phase 21 Activities

## Step-By-Step Activities
All new code lives in `LeanKernel.Logic` unless explicitly noted otherwise. Gateway-only items (transport, endpoint, DI wiring) are in `LeanKernel.Gateway`.

### A. Ingestion Core (`LeanKernel.Logic`)

1. **Define document models and contracts**
    - `DocumentIngestionJob` record with a `Source` discriminator enum (`ChannelAttachment`, `WatchedFile`, `Upload`) instead of separate variant types:  
      `DocumentIngestionJob(string FilePath, string FileName, string ContentType, Guid TenantId, Guid UserId, Guid PersonId, Guid ChannelId, DocumentAvailabilityScope AvailabilityScope, DocumentIngestionSource Source)`
    - `IDocumentIngestionQueue` interface as durable queue abstraction backed by DB: `EnqueueAsync(DocumentIngestionJob, CancellationToken)`, `TryClaimNextAsync(workerId, leaseDuration, CancellationToken)` returning `DocumentIngestionJob?`, `CompleteAsync(jobId, result, CancellationToken)`, `FailAsync(jobId, error, retryAt, CancellationToken)`
    - `IDocumentLibraryService` interface: `IngestDocumentAsync(DocumentIngestionJob, CancellationToken)` returning `IngestionResult` (fingerprint, success/failure, duplicate flag)
    - `IDocumentStoreClient` interface in `LeanKernel.Logic` for provider-agnostic document **catalog and search** (file storage on disk is handled separately by `DocumentLibraryService`): `ExistsAsync(scope, fingerprint)`, `UpsertAsync(scope, fingerprint, document)`, `SearchAsync(scope, query, channelIds, maxResults)`, `ListAsync(scope, channelIds, limit)`
    - Availability scope contract: `DocumentAvailabilityScope` (`tenant`, `user`, `channel`). `DocumentScopeContext` is a parameter object derived from the job identity fields and used as input to `IDocumentStoreClient` methods; it carries `TenantId`, `UserId`, `PersonId`, `ChannelId`, and `AvailabilityScope`.

2. **Implement durable `DocumentIngestionQueue` (DB-backed)**
    - Add `DocumentIngestionJobEntity` table with columns: `Id`, `TenantId`, `UserId`, `PersonId`, `ChannelId`, `AvailabilityScope`, `Source`, `FilePath`, `FileName`, `ContentType`, `Fingerprint`, `Status`, `AttemptCount`, `LastError`, `LeaseOwner`, `LeaseExpiresAt`, `NextAttemptAt`, `CreatedAt`, `UpdatedAt`.
    - `EnqueueAsync` writes a `Pending` row.
    - `TryClaimNextAsync` atomically claims one eligible job (`Pending` or retry-eligible) with lease semantics.
    - `CompleteAsync` marks `Completed`; `FailAsync` increments attempt count and schedules retry or marks `Poisoned`.
    - Optional in-memory wake signal (`SemaphoreSlim`/channel) is allowed only for low-latency worker wake-up; DB remains queue of record.

2a. **Add EF Core entity configuration for `DocumentIngestionJobEntity`**
    - Add `DbSet<DocumentIngestionJobEntity>` to `LeanKernel.Data/EntityContext.cs`.
    - Configure `OnModelCreating` with:
      - Primary key and column mappings.
      - Identity-partitioning query filter (`TenantId == currentTenantId`) consistent with `SessionEntity`, `TurnEntity`, and `EventEntity` patterns.
      - Index on `(Status, NextAttemptAt, LeaseExpiresAt)` for efficient queue polling.

3. **Implement `DocumentLibraryService`**
    - Resolve identity scope from job and availability scope from job metadata
    - Save the file to `{Files:RootPath}/documents/{TenantId}/{Scope}/{ChannelId}/{UserId}/{Fingerprint[0..2]}/{Fingerprint[2..4]}/{FileName}` so it survives restarts and is accessible via `file_read`
    - Compute SHA-256 fingerprint from content bytes (stream from disk to bound memory)
    - Check existence via `IDocumentStoreClient.ExistsAsync(scope, fingerprint)` — skip if exists (idempotent within the same scope)
    - Extract text via `TextExtractionHelper.ExtractAsync`; `DocumentLibraryService` resolves parameters (`scratchRoot`, `pythonExecutable`, `maxExtractedCharacters`) from `IOptions<FileSettings>`
    - Persist via `IDocumentStoreClient.UpsertAsync` with metadata: `FileName`, `ContentType`, `IngestedAt`, `ChannelId`, `TenantId`, `PersonId`, `UserId`, `AvailabilityScope`, extracted text as content
    - Return `IngestionResult` with fingerprint and duplicate flag; identical content in different channel scopes is stored as separate scoped pages

4. **Implement `DocumentIngestionHostedService`** — `BackgroundService` that polls `IDocumentIngestionQueue.TryClaimNextAsync`, invokes `IDocumentLibraryService.IngestDocumentAsync`, then calls `CompleteAsync`/`FailAsync` with retry and poison behavior. Define retry budget, poison sink location, and final disposition for failed staged files.
    - On startup, recover stale leased jobs (`InProgress` with expired lease) by returning them to `Pending`.
    - Optionally wait on in-memory wake signal between polling cycles to reduce DB polling overhead.
    - Since `DocumentLibraryService` is scoped, create a new `IServiceScope` inside `ExecuteAsync` for each claimed job using `IServiceScopeFactory` (pattern consistent with tool handler scope management in `MemorySearchTool`).

### B. Event Dispatch To Durable Queue

5. **Extend event spine contracts for generic dispatch**
    - Add generic `Emit<T>(T event)` to `IEventCollector` so any event type (including `DocumentIngestionRequestedEvent`) can be emitted without per-type method coupling. Document that `Emit<T>` is synchronous for the collector buffer; document-ingestion events are processed asynchronously by the ingestion queue subscriber at flush time.
    - Existing per-type methods (`EmitTurn`, `EmitToolCall`, `EmitTelemetry`) delegate to `Emit<T>` internally for consistent behavior. This avoids maintaining two independent emission paths.
    - Introduce `DocumentIngestionRequestedEvent` carrying `EventEnvelope` + staged file reference + document metadata + `AvailabilityScope`. Event payload must not include raw file bytes; store only staged path/reference and metadata.
    - Refactor `DbEventStore.ResolveEnvelope` to use an `IHasEnvelope` marker interface requiring `EventEnvelope Envelope { get; }` (not an empty marker) instead of a closed switch, so new event types are supported without modifying `DbEventStore`. `DocumentIngestionRequestedEvent` implements `IHasEnvelope`.
    - Set correlation/causation identifiers from request turn/tool context when available.

6. **Emit attachment ingestion events in gateway request pipeline**
    - Intercept inbound attachments **before** the MAF agent pipeline runs — either in a new Gateway middleware or in the channel terminal's request handler before forwarding to the Gateway. The raw file bytes live in the HTTP request body (multipart/form-data) or terminal payload, not in `DbChatHistoryProvider`.
    - Stage each attachment file to `{Files:RootPath}/documents/{TenantId}/channel/{ChannelId}/{UserId}/_staging/{FileName}` at this interception point.
    - Once staged, pass the staged file reference through the event spine via the existing turn event emission path (at flush time in `DbChatHistoryProvider` or equivalent).
    - Emit `DocumentIngestionRequestedEvent` with the staged file path, metadata, and `AvailabilityScope` defaulted to `channel`.
    - Resolve `tenantId`, `personId`, `userId`, `channelId` from permit/identity context.

7. **Implement event fan-out to durable job queue at flush time**
    - Define `IEventSubscriber` interface: `public interface IEventSubscriber { Task HandleAsync(IReadOnlyList<IEventEnvelope> events, CancellationToken ct); }` where `IEventEnvelope` is a non-generic base carrying `EventEnvelope`.
    - Instead of a separate event projector hosted service, the flush path in `DbChatHistoryProvider` (or a new event flush service) dispatches collected events to all registered `IEventSubscriber` handlers:
      - **Persist subscriber**: writes events to `IEventStore` (append-only audit trail). This replaces the direct `IEventStore.AppendBatchAsync` call in `DbChatHistoryProvider.StoreChatHistoryAsync`.
      - **Document ingestion subscriber**: filters events for `DocumentIngestionRequestedEvent`, translates to `DocumentIngestionJob` (with `Source = ChannelAttachment`), and enqueues to durable DB queue via `IDocumentIngestionQueue.EnqueueAsync`.
    - The flush path calls `IEventCollector.ConsumeAll()`, then dispatches the collected batch to all `IEventSubscriber` handlers.
    - No persisted-event read-back is required; the durable queue directly feeds the ingestion worker.
    - If enqueue fails, log actionable diagnostics and fail request-scoped flush behavior per policy (fail closed for attachment ingestion path).
    - Register subscribers in `IServiceCollectionExtensions` as scoped handlers; the flush path resolves them via `IEnumerable<IEventSubscriber>`.

### C. Document Library Watcher + Upload

8. **Implement `WatchFolderHostedService` for document library sources**
    - Reads `Files:WatchFolders` — a list of `{ "Path": "...", "TenantId": "...", "PersonId": "...", "UserId": "...", "ChannelId": "...", "AvailabilityScope": "tenant|user|channel", "FilePattern": "*" }`
    - Uses `FileSystemWatcher` per configured path with stability detection (settle delay from `WatchSettleDelaySeconds`). Note: `FileSystemWatcher` on macOS uses `kqueue` (per-file, not per-directory); large directory trees may hit file descriptor limits. Linux vs macOS default buffer sizes and event coalescing behavior differ. Make `WatchSettleDelaySeconds` and the reconciliation scan interval configurable.
    - On file create: wait for stability, resolve static configured scope, enqueue `DocumentIngestionJob` (with `Source = WatchedFile`)
    - Add periodic reconciliation scan to mitigate dropped `FileSystemWatcher` events

9. **Add Gateway API endpoint `POST /api/documents/upload`**
    - Primary upload path for document library ingestion (not attachment fallback)
    - Accepts multipart form: file bytes **+ required `channel_id`** + optional `availability_scope` (`user|channel|tenant`, default `user`)
    - Authenticates via the same bearer-token/channel-credential flow as `/v1/responses`
    - Resolves scope fields from `IPermit` and validates caller can write the requested scope:
      - `user`: always permitted.
      - `channel`: permitted when `channel_id` matches a channel the caller is a member of (validated via `IPermit.ChannelId` match or channel membership check).
      - `tenant`: permitted only when `IPermit.Badge` indicates admin/owner role.
    - Stages the file to `{Files:RootPath}/documents/{TenantId}/{Scope}/{ChannelId}/{UserId}/_staging/{FileName}` and enqueues `DocumentIngestionJob` (with `Source = Upload`) to durable queue via `IDocumentIngestionQueue.EnqueueAsync` directly (not through the event spine, since this is a direct user action, not a turn-time attachment).
    - Returns `202 Accepted` with enqueue payload (for example `{ "jobId": "...", "status": "queued" }`); do not return fingerprint/duplicate until job completion
    - Dependencies (`IPermit`, `IDocumentIngestionQueue`) are resolved via constructor injection in the Minimal API endpoint delegate or via `HttpContext.RequestServices` if the delegate is static. `IPermit` is available from Gateway DI (`RequestContextPermit`). `IDocumentIngestionQueue` is registered in `LeanKernel.Logic` DI and referenced by the Gateway project.

10. **Keep channel terminals attachment behavior unchanged for transport**
    - Signal/Teams continue sending attachments in request payloads for turn-time model context
    - No terminal-side watch-folder persistence required for ingestion path

### D. Search Tool

11. **Implement `DocumentSearchTool`**
    - `document_search(query, channel_ids?)` — resolve effective readable set via `IChannelMemoryPolicyResolver` (note: this resolves channel visibility from the perspective of the current request's channel only; a user in Channel A cannot search documents from Channel B even if they have access to both separately — this constraint matches existing `GBrainMemoryClient` behavior and is a known limitation documented in risk register R15)
    - If `channel_ids` is provided, require it to be a subset of the caller's readable set; deny request when any channel is unauthorized
    - Calls `IDocumentStoreClient` abstraction rather than invoking `IGBrainMcpClient` directly from logic
    - Apply availability-scope filtering before returning results:
      - `tenant`: tenant-visible documents only when caller has tenant-level read permission
      - `user`: caller's own user-scoped documents only
      - `channel`: policy-resolved channel-visible documents only
    - Aggregates results by fingerprint, keeping highest score per document
    - Returns top-N results with metadata (FileName, ContentType, IngestedAt, excerpt)

12. **Implement `DocumentListTool`**
    - `document_list(channel_id?, person_id?)` — lists documents in a given scope
    - When `channel_id` is omitted, lists documents from all readable channels resolved via `IChannelMemoryPolicyResolver`
    - Explicit `channel_id` must pass authorization check (subset of readable channels)
    - Adds `availability_scope?` filter and enforces the same scope constraints as search

### E. Gateway Transport Implementation

13. **Implement Gateway document transport (`LeanKernel.Gateway`)**
    - Add `GBrainDocumentStoreClient` implementing `IDocumentStoreClient`
    - Compose document keys/namespaces in Gateway only, encoding availability scope + identity dimensions (`documents/{TenantId}/{Scope}/{ChannelId}/...`)
    - Map `SearchAsync`/`ListAsync` to GBrain search/list calls with document namespace filtering
    - Keep all GBrain-specific payload shaping and tool names in Gateway to preserve ADR boundary

### F. Registration and Configuration

14. **Wire DI (all co-located in `LeanKernel.Logic` unless noted)**
    - Register `IDocumentIngestionQueue` → `DocumentIngestionQueue` (singleton)
    - Register `IDocumentLibraryService` → `DocumentLibraryService` (scoped)
    - Register `DocumentIngestionHostedService` as hosted service
    - Register `WatchFolderHostedService` as hosted service
    - Register event subscribers (persist subscriber, document-ingestion subscriber) as scoped handlers in the flush path
    - Register `DocumentIngestionJobRepository`/queue persistence and startup recovery service for stale lease reset
    - In Gateway `IServiceCollectionExtensions`: register `IDocumentStoreClient` → `GBrainDocumentStoreClient`
    - Add `ValidateOnStart()` for `AgentSettings.Tools.DocumentIngestion`

15. **Register tools in `IServiceProviderExtensions` via `RegisterDocumentToolsAsync`**
    - Add a new `RegisterDocumentToolsAsync` method called after `RegisterMemoryToolsAsync` in the `RegisterToolsAsync` flow
    - Registers `document_search` and `document_list` tool definitions behind `Agents:Tools:DocumentIngestion:Enabled` gate

16. **Add appsettings configuration (Gateway `appsettings.json`)**
    - Add `Agents:Tools:DocumentIngestion` section (Enabled, MaxConcurrentJobs, QueueCapacity, EnqueueTimeoutSeconds, WatchSettleDelaySeconds, WatchMaxRetries, WatchRetryBaseDelaySeconds, WatchRetryMaxDelaySeconds)
    - Add `Files:WatchFolders` section
    - Add `Files:RootPath` (already exists, defaults to `./data`)
    - Add document upload size limits and allowed availability-scope defaults
    - Validate at startup

### G. Testing

17. **Unit tests**
    - `DocumentIngestionQueueTests`: enqueue, claim leasing, completion/failure transitions, retry eligibility, stale lease recovery
    - `DocumentLibraryServiceTests`: fingerprint computation, deduplication, document transport interaction, file storage layout
    - `DocumentSearchToolTests`: policy resolution, explicit channel-id authorization, availability-scope filtering, result aggregation
    - `DocumentListToolTests`: channel_id omitted vs explicit, authorization, scope filtering
    - `DocumentIngestionHostedServiceTests`: job processing lifecycle, retry, poison handling
    - `WatchFolderHostedServiceTests`: static scope mapping and reconciliation scan
    - `EventFanOutTests`: generic Emit<T>, flush-time subscriber fan-out, document ingestion subscriber enqueue behavior

18. **Integration tests**
    - End-to-end attachment path: channel request with attachment → event emitted → durable job persisted → worker claims/processes → search
    - End-to-end document-library path: upload or watched-folder file → process → search/list
    - Policy enforcement: document stored in channel A is invisible from channel B without policy grant
    - Availability enforcement: `tenant`/`user`/`channel` scoped documents are discoverable only within allowed scope
    - Scope-write enforcement: users can upload with `user` and `channel` scope; tenant-scope upload is denied for non-admin callers
    - Upload API: `POST /api/documents/upload` returns queued response and document is later discoverable; missing `channel_id` is rejected

### H. Closure

19. Run coverage collection and confirm >= 80% for new/changed code paths
20. Run `scripts/quality/sonarqube-scan.sh` and resolve `Blocker`, `Critical`, and `Major` findings
21. Run deep-review sub-agent using `.agents/prompts/deep-review.prompt.md` and resolve findings
22. Update `docs/features/` with document ingestion feature page
23. Update `docs/configuration/appsettings-reference.md` with new config sections
24. Verify Phase 05 `exit-criteria.md` already references Phase 21 (`docs/plans/phase-05-tool-expansion/exit-criteria.md:11`); if not already present, update both `exit-criteria.md` and `evidence.md` to mark folder-ingestion gate as subsumed by Phase 21
25. Complete `evidence.md` and `exit-criteria.md` approval table

## Review Focus
- Scope resolution: folder watcher and channel attachment both produce valid identity + availability scope context (`TenantId`, `UserId`, `PersonId`, `ChannelId`, `AvailabilityScope`)
- Deduplication: same content ingested twice in the same scope produces one storage entry; identical content in different scopes is stored separately
- Policy enforcement: `document_search`/`document_list` must NOT leak documents from channels the requesting identity lacks read access to
- Availability enforcement: `tenant`/`user`/`channel` availability scope must be enforced at both ingest and discovery time
- Idempotency: replaying a job (same fingerprint + same scope) is a no-op
- Transport boundary: `LeanKernel.Logic` stays provider-agnostic; all GBrain details stay in Gateway
- Namespace isolation: documents never collide with memory pages (`memory/` prefix vs `documents/` prefix)
- Channel terminal changes are backward-compatible: existing inline-attachment behavior is preserved and ingestion trigger is gateway-side via event fan-out into durable queue
- Scope-write permissions: regular users restricted to `user`/`channel` scopes; admin badge required for `tenant` scope
- Generic event infrastructure: `IEventCollector.Emit<T>`, `IHasEnvelope` marker, and `DbEventStore` generic resolution avoid per-type coupling
