# Phase 21 — Channel-Aware Document Ingestion And Memory Pipeline

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Implement a unified document ingestion pipeline that stores channel attachments and document-library uploads into identity-scoped document storage through a provider-agnostic logic contract. Channel attachments are ingested via event emission from the gateway request pipeline into a durable database-backed ingestion job table; folder watcher ingestion is reserved for document-library folders. Discovery is policy-governed through `document_search`/`document_list`, with explicit availability scope (`tenant`, `user`, `channel`) enforced at both ingest and read time. All ingested files land under `{Files:RootPath}/documents/**`.

## Scope

### In Scope
- Core ingestion pipeline in `LeanKernel.Logic`: durable ingestion job repository/table, library service, background hosted service, optional in-memory wake signal, watcher, and event collector extensions
- Document transport abstraction in `LeanKernel.Logic` (for document put/get/search/list) with Gateway-owned GBrain implementation
- Event dispatch for channel attachment trigger: attachments are intercepted and staged to disk **before** the MAF agent pipeline (in Gateway middleware or terminal request handler), then emitted via `DocumentIngestionRequestedEvent` at the flush point alongside turn events
- Event flush persists to `IEventStore` (audit trail) and writes document ingestion jobs to a durable DB queue table; worker claims jobs from DB
- Folder watcher trigger for document-library sources (`Files:WatchFolders`) with static scope configuration
- User upload support for document-library ingestion with availability scope selection: `user` or `channel` (regular users), plus `tenant` (admins only)
- All incoming files (attachments, uploads, watched) stored under `{Files:RootPath}/documents/**` with scoped subdirectories
- Fingerprint-based deduplication (SHA-256 content hash) for idempotent ingestion
- `document_search` tool that fans out across readable channel document scopes via `IChannelMemoryPolicyResolver` through a transport abstraction, not direct GBrain calls from logic
- `document_list` tool for enumerating ingested documents per scope
- Document storage keying that includes availability scope dimensions and identity keys, composed by the Gateway transport layer
- Text extraction reuse from `TextExtractionHelper` (Phase 05) for parsing ingested files
- Startup configuration validation for `Agents:Tools:DocumentIngestion` settings
- Identity scoping via `IdentityContext` (Phase 20) throughout the pipeline
- Integration tests for ingestion, deduplication, and policy-scoped search
- Retry, poison handling, and staged-file cleanup/move policy for operational durability

### Out of Scope
- 5W1H fact extraction from document content (deferred to Phase 07 learning pipeline)
- Knowledge UI (Phase 09 — Blazor)
- Distributed/out-of-process ingestion queue
- Document versioning, diffing, or lifecycle management
- Bulk backfill service from arbitrary directories (simple folder watcher is sufficient for v1)
- Direct document transport calls into GBrain MCP client from `LeanKernel.Logic`
- Terminal-side attachment sidecar manifests or attachment-volume watch dependencies
- Phase 05's other tool gates (filesystem, data, internet, browser — already complete)

## Entry Criteria
- Phase 01 tool runtime (`IToolRegistry`, tool governance) is operational.
- Phase 05 `TextExtractionHelper` and `FileSystemSupport` are available.
- Phase 20 identity/policy contracts (`IdentityContext`, `IPolicyContext`, `IPolicyEvaluator`) are shipped.
- Channel memory policy infrastructure (`IChannelMemoryPolicyResolver`, `ChannelMemoryPolicyResolution`) is deployed.
- GBrain MCP client (`IGBrainMcpClient`) and Gateway memory transport patterns are available for implementing document transport.
- `DocumentIngestionToolSettings` configuration class already exists in `LeanKernel.Logic`.
- Event spine contracts (`EventEnvelope`, event collector/store) are available and will be extended with generic emit and generic envelope resolution.
- Document library watch folders are available to the gateway at configured paths.

## Design Detail: Availability Scope
- Upload surfaces (channel-attachment event path and document-library upload path) must assign one availability scope: `tenant`, `user`, or `channel`.
- `tenant` scope: document is discoverable by identities with tenant-level read permission, regardless of user/channel. Only granted to admin callers.
- `user` scope: document is discoverable only for the uploading/resolved user identity (across that user's allowed channels). Available to all authenticated callers.
- `channel` scope: document is discoverable only within the current channel visibility set resolved by `IChannelMemoryPolicyResolver`. Available to all authenticated callers; requires `channel_id` to match a readable channel.
- `document_search` and `document_list` must apply both policy and availability-scope filters; neither explicit `channel_ids` nor tool parameters may broaden availability beyond stored scope.

## Design Detail: Durable Job Queue
- Inbound attachments are intercepted **before** the MAF agent pipeline (in a new Gateway middleware or terminal request handler) where raw file bytes are available. Files are staged to `{Files:RootPath}/documents/{TenantId}/channel/{ChannelId}/{UserId}/_staging/{FileName}` and a staged-file reference is passed through the event spine. `DocumentIngestionRequestedEvent` is emitted at the flush point alongside turn events.
- `IEventCollector` receives a generic `Emit<T>(T event)` method so any event type — including `DocumentIngestionRequestedEvent` — can be emitted without hardcoding per-type methods.
- At flush time (`ConsumeAll`), events fan out to registered handlers:
  - `IEventStore` handler persists all events (append-only audit trail).
  - Document-ingestion handler writes `DocumentIngestionJobEntity` rows (status `Pending`) into a durable DB-backed queue.
- `DocumentIngestionHostedService` claims pending jobs from DB (lease/lock), processes them asynchronously, and updates status (`InProgress`, `Completed`, `Failed`, `Poisoned`).
- Optional in-memory wake signal may be used only to reduce polling latency; DB remains the queue of record.
- `DbEventStore` uses generic envelope resolution (marker interface `IHasEnvelope` or convention-based extraction) instead of a closed switch, so new event types are supported without modifying `DbEventStore`.
- The entire event pipeline (collector, queue, subscribers) is co-located in `LeanKernel.Logic` for a single project boundary.

## Design Detail: Scope-Write Permissions
- `POST /api/documents/upload` accepts `channel_id` (required) and `availability_scope` (optional, defaults to `user`).
- `user` scope: always permitted; the document is stored under the caller's user identity.
- `channel` scope: permitted when the caller is a member of the specified `channel_id`; resolved via `IPermit` channel membership check.
- `tenant` scope: permitted only when the caller has admin/owner badge; resolved via `IPermit.Badge`.

## Design Detail: File Storage Layout
- All ingested files (channel attachments, manual uploads, watched-folder files) are saved under `{Files:RootPath}/documents/`.
- Subdirectory layout: `{Files:RootPath}/documents/{TenantId}/{Scope}/{ChannelId}/{UserId}/{Fingerprint[0..2]}/{Fingerprint[2..4]}/{FileName}`.
- This ensures files survive restarts and are discoverable for `file_read` tool access after ingestion.
- Watched folders under `Files:WatchFolders` are monitored by `WatchFolderHostedService`; files landing there are ingested with their configured scope.

## Exit Criteria
Ingested documents are stored in the correct identity scope, survive restarts, are discoverable only through policy-resolved channels, and are searchable via the `document_search` tool. See `exit-criteria.md`.

## Roles
- Owner:
- Reviewer:
- Approver:
