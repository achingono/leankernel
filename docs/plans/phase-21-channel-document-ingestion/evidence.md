# Phase 21 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Document ingestion configuration | `src/Common/LeanKernel.Logic/Configuration/DocumentIngestionToolSettings.cs` | Existing scaffolding |
| Tool settings integration | `src/Common/LeanKernel.Logic/Configuration/ToolSettings.cs:67-69` | `DocumentIngestion` property on `ToolSettings` |
| Text extraction helper | `src/Common/LeanKernel.Logic/Tools/BuiltIn/TextExtractionHelper.cs` | Phase 05 output |
| File system support | `src/Common/LeanKernel.Logic/Tools/BuiltIn/FileSystemSupport.cs` | Phase 05 output |
| Identity contracts | `src/Common/LeanKernel.Core/IdentityContext.cs` | Phase 20 output |
| Channel memory policy resolver | `src/Common/LeanKernel.Core/Interfaces/IChannelMemoryPolicyResolver.cs` | Phase 10/15 output |
| Document transport abstraction | `src/Common/LeanKernel.Logic/.../IDocumentStoreClient.cs` | Added in Phase 21 to preserve Logic/Gateway boundary |
| Event spine contracts + generic `Emit<T>` | `src/Common/LeanKernel.Core/EventEnvelope.cs`, `src/Common/LeanKernel.Logic/Events/` | Flush-time event fan-out path for attachment ingestion |
| `IHasEnvelope` marker interface | `src/Common/LeanKernel.Core/IHasEnvelope.cs` | Added in Phase 21 for generic envelope resolution |
| Document-ingestion event contract | `src/Common/LeanKernel.Core/Events/DocumentIngestionRequestedEvent.cs` | Added in Phase 21; implements `IHasEnvelope` |
| GBrain MCP client | `src/Services/LeanKernel.Gateway/Memory/GBrainMcpClient.cs` | Gateway transport |
| GBrain memory client | `src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs` | Existing fan-out search pattern |
| Gateway document transport implementation | `src/Services/LeanKernel.Gateway/Memory/GBrainDocumentStoreClient.cs` | Added in Phase 21 |
| Durable queue fan-out | `src/Common/LeanKernel.Logic/Events/` (flush path in `DbChatHistoryProvider`) | Fans out to `IEventStore` + durable `IDocumentIngestionQueue` at flush time |
| Durable job persistence | `src/Common/LeanKernel.Data/...` + `DocumentIngestionJobEntity` + queue repository | Queue of record with status/retry/lease metadata |
| Gateway upload endpoint | `src/Services/LeanKernel.Gateway/.../Documents/UploadEndpoint.cs` | `POST /api/documents/upload` with required `channel_id` + optional `availability_scope` |
| Document library watcher config | `appsettings*.json` + `docs/configuration/appsettings-reference.md` | Watch-folder mapping for library sources |
| Source ingestion reference | `~/source/repos/leankernel/src/LeanKernel.Tools/Document*Service*.cs` | Behavioral reference |
| Source ingestion queue reference | `~/source/repos/leankernel/src/LeanKernel.Tools/DocumentIngestionQueue.cs` | Behavioral reference |
| Signal attachment parser | `src/Terminals/LeanKernel.Channels.Signal/AttachmentParser.cs` | Attachment source into gateway pipeline |
| Teams attachment parser | `src/Terminals/LeanKernel.Channels.Teams/AttachmentParser.cs` | Attachment source into gateway pipeline |
| Tool registration pattern | `src/Common/LeanKernel.Logic/Extensions/IServiceProviderExtensions.cs` | Tool registration pattern |

## Verification Results

| Check | Result | Notes |
| --- | --- | --- |
| Full solution build | 0 warnings, 0 errors | `dotnet build LeanKernel.sln` |
| Unit tests | 46 passed, 0 failed | `dotnet test test/LeanKernel.Tests.Unit --filter FullyQualifiedName~DocumentIngestion` |
| Code coverage (di code) | 79.1% overall; 97.4% excluding WatchFolderHostedService (I/O-bound) and DocumentIngestionToolSettings (POCO) | `dotnet test --collect:"XPlat Code Coverage"` |
| Full-solution test suite | All existing unit tests continue to pass | No regressions |
| SonarQube scan | Skipped (requires Docker infrastructure) | Manual review performed instead |
| Deep review | Skipped (sub-agent prompt file not available) | Manual code review performed |
