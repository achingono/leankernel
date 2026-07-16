# Phase 05 Tool Expansion — Implementation Plan

## Phase 10 Verification

**Status: Partially complete — gaps remain but core is solid.**

### What's Done
| Deliverable | Status |
|---|---|
| Person identity model (`UserEntity.PersonId`) | DONE |
| EF migration `AddUserPersonId` | DONE |
| `IPermit.PersonId` + `RequestContextPermit` | DONE |
| `TenantResolutionMiddleware` writes PersonId | DONE |
| `MemoryScope` carries `PersonId` + `ChannelId` | DONE |
| Memory key format `memory/{tenantId}/{personId}/{channelId}/{key}` | DONE |
| `LinkUsersAsync` / `UnlinkUserAsync` (C1/M1 fixes applied) | DONE |
| Cross-channel read fan-out via policy (directional AND) | DONE |
| Read-time asymmetric overlay (non-destructive) | DONE |
| Unit tests for link/unlink/cluster isolation | DONE |
| Documentation updated | DONE |

### What's Missing (not in scope for this session)
| Deliverable | Status |
|---|---|
| Verified linking flow (one-time code) | NOT DONE |
| Preference profile store | NOT DONE |
| Memory key migration script | NOT DONE |
| Reconciliation pass on policy widening | NOT DONE |
| Conflict flagging for irreconcilable facts | NOT DONE |
| Configuration + startup validation | NOT DONE |
| Exit criteria gate sign-off | NOT DONE |
| `findings.md` verdict still says "No-Go" (stale) | STALE |

**The C1 and M1 findings from the deep review have been fixed in code but `findings.md` was not updated.** The findings.md should be updated to reflect the fixes and regression tests.

---

## Phase 05 Implementation Steps

### Adaptation Strategy

The source repo uses a monolithic `LeanKernelConfig` with sub-sections. The worktree uses separate typed configs (`FileSettings`, `ToolSettings`, `AgentSettings`). Each tool must be adapted:

| Source Pattern | Worktree Equivalent |
|---|---|
| `LeanKernelConfig.FileSystem.AllowedRoot` | `FileSettings.RootPath` |
| `LeanKernelConfig.FileSystem.ScratchRoot` | New: `FileSettings.ScratchRoot` |
| `LeanKernelConfig.FileSystem.MaxDownloadBytes` | New: `FileSettings.MaxDownloadBytes` |
| `LeanKernelConfig.FileSystem.MaxExtractedCharacters` | New: `FileSettings.MaxExtractedCharacters` |
| `LeanKernelConfig.FileSystem.PythonExecutable` | New: `FileSettings.PythonExecutable` |
| `LeanKernelConfig.DatabaseQuery` | New: `ToolSettings.DatabaseQuery` |
| `LeanKernelConfig.Webwright` | New: `ToolSettings.Webwright` |
| `ToolArgumentReader.GetString()` returns `""` | Worktree returns `null` — all null checks must use `string.IsNullOrWhiteSpace()` |
| `ToolArgumentReader.GetInt32OrDefault()` | Worktree has `GetInt()` returning `int?` |
| `ToolArgumentReader.GetBoolOrDefault()` | Worktree has `GetBool()` returning `bool?` |
| `ToolArgumentReader.GetObjectDictionary()` | Worktree has `GetJson()` returning string |
| Namespace `LeanKernel.Tools.BuiltIn.*` | `LeanKernel.Logic.Tools.BuiltIn.*` |

### Step 1: Extend Configuration Models

**Files to modify:**
- `src/Common/LeanKernel.Logic/Configuration/FileSettings.cs` — add `ScratchRoot`, `MaxDownloadBytes`, `MaxExtractedCharacters`, `PythonExecutable`
- `src/Common/LeanKernel.Logic/Configuration/ToolSettings.cs` — add `DatabaseQuerySettings`, `WebwrightSettings`, `FileSystemToolSettings`

**Files to create:**
- `src/Common/LeanKernel.Logic/Configuration/DatabaseQuerySettings.cs` — `MaxRows`, `DefaultTimeoutSeconds`, `Connections` list
- `src/Common/LeanKernel.Logic/Configuration/WebwrightSettings.cs` — `Enabled`, `BaseUrl`, `ApiToken`, `RequestTimeoutSeconds`, `MaxArtifactBytes`, `HealthProbe`

### Step 2: Extend FileSystemSupport + Add TextExtractionHelper

**File to modify:**
- `src/Common/LeanKernel.Logic/Tools/BuiltIn/FileSystemSupport.cs` — add symlink detection (`HasSymlinkSegment`), `EnsureScratchPath`, `RunPythonAsync`, `IsTextLikeExtension`, `IsOcrCandidate`, `IsEpubCandidate`, `IsDocxCandidate`, `IsPptxCandidate`

**File to create:**
- `src/Common/LeanKernel.Logic/Tools/BuiltIn/TextExtractionHelper.cs` — port from source `TextExtractionHelper` class

### Step 3: Extend ToolArgumentReader

**File to modify:**
- `src/Common/LeanKernel.Logic/Tools/ToolArgumentReader.cs` — add `GetInt32OrDefault`, `GetBoolOrDefault`, `GetObjectDictionary`, `GetStringDictionary` methods to match source API

### Step 4: Implement Filesystem Tools (11 tools)

**Files to create in `src/Common/LeanKernel.Logic/Tools/BuiltIn/FileSystem/`:**
1. `FileReadTool.cs` — read file with text extraction
2. `FileWriteTool.cs` — write/append files
3. `FileEditTool.cs` — find-and-replace (text + regex)
4. `FileStatTool.cs` — file/directory metadata
5. `FileCopyTool.cs` — copy files/dirs within root
6. `FileMoveTool.cs` — move/rename within root
7. `FileDeleteTool.cs` — delete files/dirs
8. `FileTouchTool.cs` — create empty files / update timestamp
9. `FileChmodTool.cs` — Unix permissions (platform-gated)
10. `DirectoryListTool.cs` — list directory contents
11. `DirectoryCreateTool.cs` — create directories

Each tool follows the established pattern:
```csharp
namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

public static class FileXxxTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory) { ... }
}
```

### Step 5: Implement Internet Tools (2 tools)

**Files to create in `src/Common/LeanKernel.Logic/Tools/BuiltIn/Internet/`:**
1. `WebFetchTool.cs` — fetch URL content with SSRF protection, redirect handling, binary download + text extraction
2. `HttpRequestTool.cs` — bounded HTTP requests with headers, query params, body, redirect validation

Both reuse `EgressValidator` for SSRF protection. `WebFetchTool` depends on `TextExtractionHelper` and `FileSystemSupport.EnsureScratchPath`.

### Step 6: Implement Data Tools (3 tools)

**Files to create in `src/Common/LeanKernel.Logic/Tools/BuiltIn/Data/`:**
1. `DatabaseQueryTool.cs` — read-only SQL queries against PostgreSQL/SQLite
2. `JsonTransformTool.cs` — deterministic JSON transforms (select, project, filter_equals, sort, slice, flatten)
3. `CsvXlsxReadWriteTool.cs` — read/write CSV and XLSX files

**NuGet packages to add to `LeanKernel.Logic.csproj`:**
- `CsvHelper` (for CSV operations)
- `ClosedXML` (for XLSX operations)

**Note:** `Microsoft.Data.Sqlite` and `Npgsql` are already available transitively via `LeanKernel.Data`.

### Step 7: Implement Browser Tools (4 tools + infrastructure)

**Files to create in `src/Common/LeanKernel.Logic/Tools/BuiltIn/Browser/`:**
1. `IWebwrightClient.cs` — interface for browser sidecar HTTP client
2. `WebwrightClient.cs` — implementation using `IHttpClientFactory`
3. `WebwrightHealthProbe.cs` — health check for sidecar readiness
4. `BrowserToolDefinitions.cs` — 4 tool definitions (run_task, get_run, get_artifact, cancel_run)
5. `WebwrightException.cs` — browser-specific exception type

**Files to modify:**
- `src/Services/LeanKernel.Gateway/Extensions/IServiceCollectionExtensions.cs` — register `IWebwrightClient` + named `HttpClient` + health check

### Step 8: Implement Document Ingestion (5 services)

**Files to create in `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/`:**
1. `DocumentIngestionJob.cs` — job model (PathDocumentIngestionJob, StreamDocumentIngestionJob)
2. `IDocumentIngestionQueue.cs` — interface
3. `DocumentIngestionQueue.cs` — bounded channel-based queue
4. `IDocumentLibraryService.cs` — interface
5. `DocumentLibraryService.cs` — core ingestion: parse → markdown → GBrain save
6. `DocumentIngestionHostedService.cs` — background worker processing queue
7. `DocumentFolderIngestionHostedService.cs` — FileSystemWatcher + stability detection + dedupe
8. `DocumentBackfillService.cs` — bulk backfill with checkpoint-resume

**Note:** These depend on `IMemoryService` (wiki save) and `IGrainMcpClient` (file upload). The interfaces `IDocumentFingerprintService` and `IDocumentIngestionJobRepository` will need to be defined or stubbed.

### Step 9: Register All Tools

**File to modify:**
- `src/Common/LeanKernel.Logic/Extensions/IServiceProviderExtensions.cs` — update `RegisterBuiltInTools` to register all new tools, gated by config

### Step 10: Add Unit Tests

**Files to create in `test/LeanKernel.Tests.Unit/Tools/`:**
1. `FileSystem/FileReadToolTests.cs`
2. `FileSystem/FileWriteToolTests.cs`
3. `FileSystem/FileEditToolTests.cs`
4. `FileSystem/FileStatToolTests.cs`
5. `FileSystem/FileCopyToolTests.cs`
6. `FileSystem/FileMoveToolTests.cs`
7. `FileSystem/FileDeleteToolTests.cs`
8. `FileSystem/FileTouchToolTests.cs`
9. `FileSystem/FileChmodToolTests.cs`
10. `FileSystem/DirectoryListToolTests.cs`
11. `FileSystem/DirectoryCreateToolTests.cs`
12. `Internet/WebFetchToolTests.cs`
13. `Internet/HttpRequestToolTests.cs`
14. `Data/DatabaseQueryToolTests.cs`
15. `Data/JsonTransformToolTests.cs`
16. `Data/CsvXlsxReadWriteToolTests.cs`
17. `Browser/BrowserToolDefinitionsTests.cs`
18. `DocumentIngestion/DocumentIngestionQueueTests.cs`
19. `DocumentIngestion/DocumentLibraryServiceTests.cs`

### Step 11: Update Documentation

- Update `docs/features/` tools feature page
- Update `docs/configuration/index.md` with new config sections
- Update `docs/plans/phase-05-tool-expansion/evidence.md` with output references

---

## Execution Order

1. **Config models** (Step 1) — foundation for all tools
2. **FileSystemSupport + TextExtractionHelper** (Step 2) — shared infrastructure
3. **ToolArgumentReader extension** (Step 3) — API compatibility
4. **Filesystem tools** (Step 4) — 11 tools, highest value
5. **Internet tools** (Step 5) — 2 tools, moderate complexity
6. **Data tools** (Step 6) — 3 tools, package dependencies
7. **Browser tools** (Step 7) — 4 tools + infrastructure
8. **Document ingestion** (Step 8) — 5 services, most complex
9. **Registration** (Step 9) — wire everything up
10. **Tests** (Step 10) — verify all tools
11. **Documentation** (Step 11) — finalize

Each step should be committed separately for clean history.
