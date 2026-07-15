# Phase 03 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Current turn flow (MAF wiring, memory + history providers) | `src/Services/LeanKernel.Gateway/Programs.cs`, `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`, `DbChatHistoryProvider.cs` | Rebuild maintainer |
| Identity partitioning contracts | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs`, `src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs`, `IdentityIsolationKeyProvider.cs` | Rebuild maintainer |
| Source turn pipeline reference | `~/source/repos/leankernel/src/LeanKernel.Agents/TurnPipeline.cs`, `ContinuationTurnPipeline.cs`, `TurnProgressBroker.cs` | Reviewer |
| Source context gating reference | `~/source/repos/leankernel/src/LeanKernel.Context/ContextGatekeeper.cs`, `PromptAssembler.cs`, `History/*`, `Retrieval/*` | Reviewer |
| Config shape constraints | `docs/configuration/index.md`, AGENTS.md working rules | Repository owner |

## Optional Inputs
- Source diagnostics on per-turn context snapshots (`~/source/repos/leankernel/src/LeanKernel.Diagnostics/ContextDiagnosticsService.cs`) to inform later diagnostics integration.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Source references confirmed against the actual `~/source/repos/leankernel` tree
