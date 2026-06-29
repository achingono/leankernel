# PRD: Comprehensive Code Comments & Documentation Update

## Summary

Add comprehensive code comments to all undocumented source files and update/reorganize documentation to accurately reflect the current implementation.

---

## Part 1: Code Comments

### Target: 54 source files lacking XML doc comments

#### Priority P0 — LeanKernel.Learning (10 files, 0% coverage)

| File | Action |
|------|--------|
| `src/LeanKernel.Learning/SelfImprovementPipeline.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/LearningBackgroundWorker.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/FactExtractionStep.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/CapabilityGapDetectionStep.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/EngagementTrackingStep.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/IdentityIntentExtractionStep.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/KnowledgePageUpdateCoordinator.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/TurnEventQueue.cs` | Add class + method XML docs |
| `src/LeanKernel.Learning/LearningKeys.cs` | Add class + constant XML docs |
| `src/LeanKernel.Learning/ServiceCollectionExtensions.cs` | Add method XML docs |

#### Priority P0 — Gateway Services (1 file, 672 lines)

| File | Action |
|------|--------|
| `src/LeanKernel.Gateway/Services/ChatService.cs` | Add class + all public method XML docs |

#### Priority P1 — Tools FileSystem (13 files, security-sensitive)

| File | Action |
|------|--------|
| `src/LeanKernel.Tools/BuiltIn/FileSystem/DirectoryCreateTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/DirectoryListTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/ExtractTextTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileChmodTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileCopyTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileDeleteTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileEditTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileMoveTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileReadTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileSearchTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileStatTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileTouchTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/FileSystem/FileWriteTool.cs` | Add class + method XML docs |

#### Priority P1 — Tools Common/Data (5 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Tools/BuiltIn/Common/ToolArgumentReader.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/Common/FileSystemSupport.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/Data/CsvXlsxReadWriteTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/Data/DatabaseQueryTool.cs` | Add class + method XML docs |
| `src/LeanKernel.Tools/BuiltIn/Data/JsonTransformTool.cs` | Add class + method XML docs |

#### Priority P1 — Context History (3 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Context/History/HistoryShaper.cs` | Add class + method XML docs |
| `src/LeanKernel.Context/History/ConversationCompactor.cs` | Add class + method XML docs |
| `src/LeanKernel.Context/History/HistoryCompactionStrategy.cs` | Add class + method XML docs |

#### Priority P1 — Agents Quality (6 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Agents/Quality/IQualityCheck.cs` | Add interface + method XML docs |
| `src/LeanKernel.Agents/Quality/ResponseQualityGate.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/Quality/ConstraintCoverageCheck.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/Quality/EmptyResponseCheck.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/Quality/MinLengthCheck.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/Quality/RefusalDetectionCheck.cs` | Add class + method XML docs |

#### Priority P2 — Agents Other (6 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Agents/ToolSelection/IToolSelector.cs` | Add interface XML docs |
| `src/LeanKernel.Agents/ToolSelection/NullToolSelector.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/ToolSelection/ToolSelector.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/AgentInvocationBuilder.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/Enhancement/EnhancementTextMatcher.cs` | Add class + method XML docs |
| `src/LeanKernel.Agents/Routing/ResponseQualityHeuristics.cs` | Add class + method XML docs |

#### Priority P2 — Gateway Remaining (5 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Gateway/Program.cs` | Add top-level file comment |
| `src/LeanKernel.Gateway/Endpoints.cs` | Add method XML docs |
| `src/LeanKernel.Gateway/Auth/ForwardedAuthHandler.cs` | Add class + method XML docs |
| `src/LeanKernel.Gateway/Services/DiagnosticsService.cs` | Add class + method XML docs |
| `src/LeanKernel.Gateway/Services/OnboardingService.cs` | Add class + method XML docs |

#### Priority P2 — Knowledge (3 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Knowledge/GBrainMcpClient.cs` | Add class + method XML docs |
| `src/LeanKernel.Knowledge/GBrainException.cs` | Add class + XML docs |
| `src/LeanKernel.Knowledge/KnowledgeServiceCollectionExtensions.cs` | Add method XML docs |

#### Priority P2 — Context Other (2 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Context/ConversationHistoryAssembler.cs` | Add class + method XML docs |
| `src/LeanKernel.Context/ContextServiceCollectionExtensions.cs` | Add method XML docs |

#### Priority P3 — Abstractions Enums (5 files)

| File | Action |
|------|--------|
| `src/LeanKernel.Abstractions/Enums/ContextAdmissionReason.cs` | Add enum + member XML docs |
| `src/LeanKernel.Abstractions/Enums/ContextExclusionReason.cs` | Add enum + member XML docs |
| `src/LeanKernel.Abstractions/Enums/DiagnosticCategory.cs` | Add enum + member XML docs |
| `src/LeanKernel.Abstractions/Enums/ModelTier.cs` | Add enum + member XML docs |
| `src/LeanKernel.Abstractions/Enums/QualityOutcome.cs` | Add enum + member XML docs |

#### Priority P3 — Abstractions Interface (1 file)

| File | Action |
|------|--------|
| `src/LeanKernel.Abstractions/Interfaces/IContextDiagnosticsService.cs` | Add interface + method XML docs |

### Comment Style Guidelines

- Follow existing pattern: `/// <summary>` for classes, interfaces, enums, and public methods
- Include `<param>` tags for method parameters with non-obvious semantics
- Include `<returns>` tags for non-void methods
- Use `<remarks>` for complex implementation notes
- Keep summaries concise (1-2 sentences)
- For enums, document each member
- For tool classes, describe the tool's purpose and security implications where relevant

---

## Part 2: Documentation Updates

### 2.1 — README.md (Critical)

Update the repository structure section to include all projects:

**Current (11 projects listed):**
- Abstractions, Core, Agents, Context, Knowledge, Persistence, Tools, Channels, Diagnostics, Scheduler, Gateway

**Add missing projects (7):**
- `LeanKernel.Thinker` — Agent orchestration, strategy/routing, authorization, enhancement, middleware, workflows
- `LeanKernel.Archivist` — Wiki extraction, embedding, engagement, knowledge, sessions, identity
- `LeanKernel.Commander` — Channel adapters and outbound command queue
- `LeanKernel.Host` — Blazor UI composition, API controllers, data migrations
- `LeanKernel.Learning` — Self-improvement pipeline, fact extraction, capability gap detection
- `LeanKernel.Plugins` — Built-in skills, SDK, attachments
- `LeanKernel.Generators` — Source generators (if active) or note as planned

Update project pairings to reflect actual architecture:
- Agents + Thinker (orchestration)
- Knowledge + Archivist (knowledge integration)
- Plugins + Tools (tool execution)
- Channels + Commander (routing/outbound)
- Gateway + Host (composition/UI)

### 2.2 — Solution Structure (Critical)

**File:** `docs/architecture/solution-structure.md`

- Add all 9 missing projects to the project table
- Add their responsibilities and subdirectory summaries
- Update the dependency map to include all projects
- Clarify Gateway vs Host composition root relationship
- Note LeanKernel.Core status (empty/planned)

### 2.3 — Configuration Reference (Moderate)

**File:** `docs/configuration/configuration-reference.md`

- Add `LeanKernel:Skills` section (BasePaths, Enabled)
- Verify all config sections match `LeanKernelConfig.cs` properties
- Cross-reference with `appsettings.json`

**File:** `docs/configuration/environment-variables.md`

- Add Skills-related environment variables
- Verify all env vars match actual usage

### 2.4 — API Documentation (Moderate)

**File:** `docs/api/gateway-api.md`

- Add `GET /healthz` endpoint documentation

**New File:** `docs/api/host-api.md` (or document that Host is internal)

- Document Host controllers if they are public-facing:
  - AuthController, ChatController, ConfigController, FilesController
  - LogsController, ModelLimitDriftController, OnboardingController
  - OpenAiController, RoutingConfigController, StatsController, WikiController

### 2.5 — Feature Documentation Gaps (Moderate)

**File:** `docs/features/index.md`

- Add missing feature pages to the index:
  - Document Ingestion
  - Forwarded Auth
  - Correlation ID Middleware
  - Rate Limiting
  - Skills (dynamic)

**New Files:**
- `docs/features/skills.md` — Dynamic skills system documentation
- `docs/features/middleware.md` — Gateway middleware stack documentation

### 2.6 — Host UI Documentation (Moderate)

**File:** `docs/features/ui/index.md`

- Document Host Blazor pages:
  - AgentsConfiguration, Chat, Dashboard, Files, Login, Logs
  - Onboarding, Routing, RoutingConfig, Settings, Wiki

### 2.7 — Legacy Stubs Cleanup (Low)

Remove or clearly mark redirect stubs:
- `docs/architecture/overview.md`
- `docs/architecture/architecture.md`
- `docs/architecture/key-flows.md`
- `docs/architecture/data-model.md`
- `docs/architecture/infrastructure.md`
- `docs/features/context-diagnostics-api.md`
- `docs/features/gateway-api.md`
- `docs/features/production-ops.md`
- `docs/features/scheduled-jobs-management.md`
- `docs/features/intelligent-model-routing.md`
- `docs/features/blazor-chat-ui.md`

### 2.8 — Docs Inventory Matrix (Low)

**File:** `docs/development/docs-inventory-matrix.md`

- Update status for all files reviewed
- Add new documentation pages
- Mark stale/duplicate entries clearly

---

## Execution Order

1. **Phase 1: Code Comments** — Add XML docs to all 54 undocumented source files
2. **Phase 2: README + Architecture** — Update README.md and solution-structure.md
3. **Phase 3: Configuration + API** — Update config reference and API docs
4. **Phase 4: Features + UI** — Add missing feature docs and update UI docs
5. **Phase 5: Cleanup** — Remove/mark legacy stubs, update inventory matrix

---

## Verification

After implementation:
1. Run `dotnet build src/LeanKernel.sln --no-restore -v minimal` to verify no code errors
2. Run `dotnet test src/LeanKernel.sln --no-build -v minimal --filter 'FullyQualifiedName!~Playwright'` to verify tests pass
3. Spot-check XML doc generation with `dotnet build -v detailed` for warnings
4. Review all new/updated documentation for accuracy and cross-links
