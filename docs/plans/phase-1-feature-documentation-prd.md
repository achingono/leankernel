# Phase 1 Feature Documentation PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers, contributors, and operators who need implementation-accurate Phase 1 runtime explanations.
- **Phase goal:** Publish the remaining Phase 1 feature documentation so the rearchitecture has a complete understanding-oriented set of runtime docs under `docs/features/`.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed after documenting exact source files, using the implemented metric names, clarifying that `TurnPipeline` does not directly invoke `DiagnosticsCollector`, and avoiding any claim that `ToolExecutor` enforces governance.

## Problem statement

Phase 1 shipped core runtime packages, but the feature documentation set is incomplete. The current docs cover Gateway API, authentication, and configuration, but they do not yet explain the runtime slices that make the rearchitecture work: context gating, turn execution, knowledge retrieval, tool governance, and diagnostics.

## Scope

This task will:

1. Create `docs/features/context-gating.md`.
2. Create `docs/features/turn-pipeline.md`.
3. Create `docs/features/knowledge-retrieval.md`.
4. Create `docs/features/tool-governance.md`.
5. Create `docs/features/diagnostics.md`.
6. Update `docs/features/index.md` to list all Phase 1 feature docs.
7. Update `docs/plans/index.md` to include this PRD.
8. Attempt restore, build, test, coverage, and Sonar validation before and after the doc edits, recording the local `dotnet` blocker that exists in this environment.

## Out of scope

- Changing runtime behavior or configuration contracts.
- Renaming metrics, endpoints, or configuration keys to match requested wording when the implementation differs.
- Adding new documentation types outside the explanation-oriented feature docs and the required PRD entry.
- Editing README or architecture docs unless a directly related accuracy issue is discovered.

## Source files

The new docs must stay aligned to these implementation sources:

- `src/LeanKernel.Context/ContextGatekeeper.cs`
- `src/LeanKernel.Context/ContextCandidateRetriever.cs`
- `src/LeanKernel.Context/ConversationHistoryAssembler.cs`
- `src/LeanKernel.Context/PromptAssembler.cs`
- `src/LeanKernel.Abstractions/Models/ContextBudget.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Agents/AgentRuntime.cs`
- `src/LeanKernel.Knowledge/GBrainMcpClient.cs`
- `src/LeanKernel.Knowledge/GBrainKnowledgeService.cs`
- `src/LeanKernel.Knowledge/KnowledgeServiceCollectionExtensions.cs`
- `src/LeanKernel.Tools/ToolRegistry.cs`
- `src/LeanKernel.Tools/ToolGovernancePolicy.cs`
- `src/LeanKernel.Tools/ToolExecutor.cs`
- `src/LeanKernel.Tools/ToolsServiceCollectionExtensions.cs`
- `src/LeanKernel.Tools/BuiltIn/WikiSearchTool.cs`
- `src/LeanKernel.Tools/BuiltIn/WikiReadTool.cs`
- `src/LeanKernel.Tools/BuiltIn/WikiWriteTool.cs`
- `src/LeanKernel.Diagnostics/DiagnosticsCollector.cs`
- `src/LeanKernel.Diagnostics/LeanKernelMetrics.cs`
- `src/LeanKernel.Diagnostics/LeanKernelLogEnricher.cs`
- `src/LeanKernel.Persistence/PostgresDiagnosticsSink.cs`
- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/Program.cs`
- `docs/CONTRIBUTING-DOCS.md`
- `docs/features/gateway-api.md`
- `docs/features/authentication.md`
- `docs/configuration/phase-1-config.md`

## Documentation approach

### Diátaxis quadrant

Each new file will be an **Explanation** document:

- orient readers around why the feature exists,
- clarify how the pieces fit together,
- describe trade-offs and design intent,
- avoid step-by-step tutorial framing,
- include examples only when they deepen understanding.

### Style and structure

- Use implementation-accurate terminology from the code.
- Use Markdown tables for configuration and runtime concepts.
- Use Mermaid diagrams where a flow or composition view improves clarity.
- Cross-link related feature docs and the Phase 1 configuration reference.
- Keep claims precise where the code is narrower than the roadmap intent.

## Accuracy constraints

The docs must reflect the current implementation, including these important nuances:

- `ContextBudget.FromConfig` reserves response headroom first, then allocates the remaining prompt budget by ratio.
- `ContextGatekeeper` pools wiki and retrieval budgets into one shared knowledge-admission budget, then classifies admitted items by source.
- Candidates with `Score < 0.1` are rejected with `LowRelevanceScore`.
- `ConversationHistoryAssembler` currently fits the newest turns into budget; Phase 2 compaction is still future work.
- `TurnPipeline` persists the user turn before invoking the model.
- `TurnPipeline` does **not** directly inject or call `DiagnosticsCollector`.
- `ToolGovernancePolicy` is enforced when resolving visible tools through `ToolRegistry`; `ToolExecutor` executes resolved handlers and does not perform a second governance pass.
- The implemented metrics are `leankernel.turns.processed`, `leankernel.tokens.used`, `leankernel.turn.latency`, `leankernel.quality_gate.failures`, `leankernel.escalations`, and `leankernel.budget.utilization`.

## Deliverables

### `docs/features/context-gating.md`

Explain deny-by-default context admission, budget math, pooled knowledge admission, history shaping, prompt assembly, configuration, and examples.

### `docs/features/turn-pipeline.md`

Explain the canonical six-step turn flow, session persistence, tool visibility merge, extensibility points, failure boundaries, and the adjacent diagnostics story.

### `docs/features/knowledge-retrieval.md`

Explain GBrain MCP integration, JSON-RPC transport, `IKnowledgeService` operations, built-in wiki tools, and configuration.

### `docs/features/tool-governance.md`

Explain registry-based tool discovery, visibility rules, open-default governance, built-in wiki tools, execution responsibilities, and extension paths.

### `docs/features/diagnostics.md`

Explain collector responsibilities, persisted entries, OpenTelemetry activities, metrics, Serilog enrichment, the diagnostics API, and configuration.

## Validation plan

1. Attempt `dotnet restore src/LeanKernel.sln`.
2. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
3. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
4. Attempt `scripts/quality/test-coverage.sh`.
5. Attempt `scripts/quality/sonarqube-scan.sh`.
6. Verify the new docs link to related documentation using relative paths.
7. Verify Mermaid blocks and configuration examples are well-formed.
8. Check line counts and structure for each new feature doc.

## Validation status

Initial validation attempt is blocked in this environment because `dotnet` is not installed:

- `dotnet restore src/LeanKernel.sln` → `bash: dotnet: command not found`
- `scripts/quality/test-coverage.sh` → `dotnet: command not found`

The same validation commands must be re-run in CI or on a development machine with the .NET SDK before merge.

## Acceptance criteria

- The missing Phase 1 feature docs exist under `docs/features/`.
- `docs/features/index.md` lists all implemented Phase 1 feature docs.
- All new docs follow the Explanation quadrant and cross-link related material.
- The docs use the actual implementation details, names, and constraints from the code.
- The PRD is saved under `docs/plans/` before the feature-doc edits.
- Validation evidence includes the current local `dotnet` blocker and a clear note that CI or another environment must complete the full quality run.
