# Phase 21 Inputs

## Required Inputs

| Input | Source | Owner |
| --- | --- | --- |
| `DocumentIngestionToolSettings.cs` | `src/Common/LeanKernel.Logic/Configuration/DocumentIngestionToolSettings.cs` | Rebuild |
| `TextExtractionHelper` | `src/Common/LeanKernel.Logic/Tools/BuiltIn/TextExtractionHelper.cs` (Phase 05) | Rebuild |
| `FileSystemSupport` | `src/Common/LeanKernel.Logic/Tools/BuiltIn/FileSystemSupport.cs` (Phase 05) | Rebuild |
| `IdentityContext` | `src/Common/LeanKernel.Core/IdentityContext.cs` (Phase 20) | Rebuild |
| `IChannelMemoryPolicyResolver` | `src/Common/LeanKernel.Core/Interfaces/IChannelMemoryPolicyResolver.cs` | Rebuild |
| `ChannelMemoryPolicyResolution` | `src/Common/LeanKernel.Core/Entities/ChannelMemoryPolicyResolution.cs` | Rebuild |
| `IDocumentStoreClient` (new) | `src/Common/LeanKernel.Logic/...` (to be introduced in this phase) | Rebuild |
| Event spine contracts (`EventEnvelope`, `IEventCollector` with generic `Emit<T>`, `IEventStore`) | `src/Common/LeanKernel.Core/EventEnvelope.cs`, `src/Common/LeanKernel.Logic/Events/*` | Rebuild |
| `IHasEnvelope` marker interface (new) | `src/Common/LeanKernel.Core/` (to be introduced in this phase) | Rebuild |
| `IGBrainMcpClient` | `src/Services/LeanKernel.Gateway/Memory/GBrainMcpClient.cs` | Rebuild |
| `GBrainMemoryClient` | `src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs` | Rebuild |
| `IMemoryClient` | `src/Common/LeanKernel.Logic/Providers/IMemoryClient.cs` | Rebuild |
| Durable ingestion job persistence pattern | `src/Common/LeanKernel.Data/*` + existing EF Core entity/repository conventions | Rebuild |
| `IPermit` / `RequestContextPermit` | Phase 19/20 | Rebuild |
| Gateway attachment normalization path | Gateway request/turn handling path where inbound attachments are available | Rebuild |
| Document library watch-folder topology | Gateway host/container folder mappings for `Files:WatchFolders` | Rebuild |
| Source document ingestion reference | `~/source/repos/leankernel/src/LeanKernel.Tools/Document*Service*.cs` | Source repo |
| Source document ingestion queue | `~/source/repos/leankernel/src/LeanKernel.Tools/DocumentIngestionQueue.cs` | Source repo |

## Optional Inputs
- Phase 06 channel terminal attachment parsers (`LeanKernel.Channels.Signal/AttachmentParser.cs`, `LeanKernel.Channels.Teams/AttachmentParser.cs`)

## Input Validation Checklist
- [x] `DocumentIngestionToolSettings` config class exists with `Enabled`, `MaxConcurrentJobs`, `QueueCapacity`, `EnqueueTimeoutSeconds`, `WatchSettleDelaySeconds`, `WatchMaxRetries`, `WatchRetryBaseDelaySeconds`, `WatchRetryMaxDelaySeconds`
- [x] `TextExtractionHelper` provides `ExtractAsync(path, scratchRoot, pythonExecutable, maxExtractedCharacters, ct)` for document text extraction
- [x] Identity contracts from Phase 20 are in `main` with `IdentityContext`, `IPolicyContext`, `IPolicyEvaluator`
- [x] `IChannelMemoryPolicyResolver.ResolveAsync` returns `ReadableChannelIds` for document search fan-out
- [ ] `IDocumentStoreClient` contract defined in `LeanKernel.Logic` with no GBrain-specific payloads
- [ ] `DocumentIngestionRequestedEvent` contract defined and implements `IHasEnvelope` marker for generic envelope resolution
- [ ] `IHasEnvelope` marker interface defined in `LeanKernel.Core` for generic `DbEventStore` resolution
- [ ] `IEventCollector.Emit<T>` generic method added for type-agnostic event emission
- [ ] Durable ingestion job table/entity defined with status + retry + lease fields
- [ ] Startup recovery path reclaims stale leased jobs and resumes processing
- [ ] Availability-scope model (`tenant|user|channel`) is defined for ingestion and discovery
- [ ] Source ingestion code is available for behavioral reference (not a hard blocker)
- [ ] Document-library watch-folder paths are validated for gateway runtime
- [ ] `{Files:RootPath}` directory exists or is created at startup with `documents/` subdirectory
