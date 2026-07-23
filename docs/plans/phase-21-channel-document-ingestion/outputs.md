# Phase 21 Outputs

## Mandatory Outputs

| Output | Description | Format / Location |
| --- | --- | --- |
| `DocumentIngestionJob.cs` | Job model with `Source` discriminator enum (`ChannelAttachment`, `WatchedFile`, `Upload`) | C# record in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `DocumentIngestionSource.cs` | Source discriminator for ingestion jobs | C# enum in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `DocumentAvailabilityScope.cs` | Availability scope enum (`tenant`, `user`, `channel`) | C# enum in `LeanKernel.Core` |
| `DocumentScopeContext.cs` | Scope parameter object derived from job identity fields, passed to `IDocumentStoreClient` | C# record in `LeanKernel.Logic/Providers/` |
| `IDocumentIngestionQueue.cs` | Durable queue contract (enqueue/claim/complete/fail) | C# interface in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `DocumentIngestionQueue.cs` | DB-backed queue implementation with lease semantics | C# class in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `DocumentIngestionJobEntity.cs` | Durable ingestion job row model with status/retry/lease metadata | C# entity in `LeanKernel.Core/Entities/DocumentIngestionJobEntity.cs` (EF Core configuration in `LeanKernel.Data/EntityContext.cs`; identity-partitioning query filters consistent with `SessionEntity`/`TurnEntity`) |
| `IDocumentLibraryService.cs` | Ingestion service contract | C# interface in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `DocumentLibraryService.cs` | Core ingestion: extract → fingerprint → dedupe → persist to document store + file system | C# class in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `IDocumentStoreClient.cs` | Provider-agnostic document **catalog and search** contract (file storage on disk is handled separately by `DocumentLibraryService`) | C# interface in `LeanKernel.Logic/Providers/` |
| `GBrainDocumentStoreClient.cs` | Gateway-owned GBrain implementation for document store/search/list | C# class in `LeanKernel.Gateway/Memory/` |
| `IngestionResult.cs` | Result model (fingerprint, success, duplicate flag) | C# record in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `DocumentIngestionHostedService.cs` | Background worker processing the ingestion queue | C# `BackgroundService` in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `WatchFolderHostedService.cs` | `FileSystemWatcher`-based folder monitor with scope configuration | C# `BackgroundService` in `LeanKernel.Logic/Tools/DocumentIngestion/` |
| `WatchFolderConfiguration.cs` | Per-folder scope mapping model | C# class in `LeanKernel.Logic/Configuration/` |
| `DocumentIngestionRequestedEvent.cs` | Event contract for attachment-driven ingestion; implements `IHasEnvelope` | C# record in `LeanKernel.Core/Events/` |
| `IHasEnvelope.cs` | Marker interface requiring `EventEnvelope Envelope { get; }` for generic envelope resolution in `DbEventStore` | C# interface in `LeanKernel.Core` |
| Event fan-out to durable queue | `IEventCollector.Emit<T>` → flush dispatches to `IEventStore` + `IDocumentIngestionQueue.EnqueueAsync` subscriber | Logic in `LeanKernel.Logic` event flush path |
| Startup recovery logic | Requeue stale leased jobs on service start | Logic hosted service/repository path |
| Document library watch folder mapping | Configured gateway watch paths for document library sources | Gateway `appsettings*.json` and docs |
| `DocumentSearchTool.cs` | Policy-scoped document search tool (`RegisterDocumentToolsAsync`) | C# tool definition in `LeanKernel.Logic/Tools/DocumentIngestion/DocumentSearchTool.cs`, namespace `LeanKernel.Logic.Tools.DocumentIngestion` |
| `DocumentListTool.cs` | Policy-scoped document listing tool (`RegisterDocumentToolsAsync`) | C# tool definition in `LeanKernel.Logic/Tools/DocumentIngestion/DocumentListTool.cs`, namespace `LeanKernel.Logic.Tools.DocumentIngestion` |
| Document upload API | `POST /api/documents/upload` with required `channel_id` + optional `availability_scope` | ASP.NET Minimal API in Gateway |
| DI registration updates | `IServiceCollectionExtensions.cs` changes (all Logic co-located; Gateway adds transport) | Service registration |
| Tool registration updates | `RegisterDocumentToolsAsync` in `IServiceProviderExtensions.cs` | Tool registration |
| Unit tests | Queue, library, search, list, hosted service, watcher, event fan-out tests | `test/LeanKernel.Tests.Unit/DocumentIngestion/` |
| Integration tests | End-to-end ingestion + policy-scoped search + scope-write enforcement tests | `test/LeanKernel.Tests.Integration/DocumentIngestion/` |
| Configuration docs update | `docs/configuration/appsettings-reference.md` | Markdown |
| Feature docs | `docs/features/document-ingestion.md` | Markdown |
| SonarQube evidence | Quality scan with no unresolved Blocker/Critical/Major issues | Evidence entry in `docs/plans/phase-21-channel-document-ingestion/evidence.md` |
| Deep review evidence | Deep-review sub-agent output and remediation log | Evidence entry in `docs/plans/phase-21-channel-document-ingestion/evidence.md` |

## Optional Outputs
- Ingestion status endpoint for queued jobs (if async status polling is needed in v1)

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
- [ ] Unit test coverage ≥ 80% for new code
- [ ] Integration tests cover cross-channel policy enforcement
- [ ] Availability scope (`tenant|user|channel`) is enforced in upload, ingestion, search, and list paths
- [ ] `POST /api/documents/upload` returns queued semantics (job id/status), not completion semantics
- [ ] `POST /api/documents/upload` requires `channel_id` parameter; scope-write permissions validated per caller badge
- [ ] `IEventCollector.Emit<T>` generic method added; existing per-type methods (`EmitTurn`, `EmitToolCall`, `EmitTelemetry`) delegate to `Emit<T>` internally
- [ ] `IEventSubscriber` interface defined and registered in DI; flush path dispatches to all subscribers via `IEnumerable<IEventSubscriber>`
- [ ] `DbEventStore.ResolveEnvelope` uses `IHasEnvelope` marker interface (`EventEnvelope Envelope { get; }`) instead of closed switch
- [ ] Hosted services (`DocumentIngestionHostedService`, `WatchFolderHostedService`) co-located in `LeanKernel.Logic`
- [ ] `DocumentIngestionHostedService` creates `IServiceScope` via `IServiceScopeFactory` per claimed job since `DocumentLibraryService` is scoped
- [ ] `RegisterDocumentToolsAsync` registered after `RegisterMemoryToolsAsync` in tool registration flow
- [ ] Event fan-out writes to `IEventStore` + durable `IDocumentIngestionQueue` at flush time
- [ ] Durable queue supports claim lease, retry scheduling, poison status, and stale lease recovery on startup
- [ ] All ingested files saved under `{Files:RootPath}/documents/**`
- [ ] `document_list` when `channel_id` is omitted returns documents from all readable channels
- [ ] Deep review completed and issues resolved
- [ ] SonarQube scan completed and all Blocker/Critical/Major issues resolved
