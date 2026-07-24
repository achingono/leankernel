# Phase 21 Delta + Phase 07 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Phase 21 document ingestion pipeline | `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/` | Phase 21 |
| `FactExtractionService` | `src/Common/LeanKernel.Logic/Memory/FactExtractionService.cs` | Phase 03/05 |
| `MemoryPageNormalizer`, `MemoryPageLinker` | `src/Common/LeanKernel.Logic/Memory/` | Phase 03/05 |
| `IEventCollector`, `IEventSubscriber`, `DbEventStore` | `src/Common/LeanKernel.Logic/Events/` | Phase 20/21 |
| `DocumentIngestionHostedService` | `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/` | Phase 21 |
| `IDocumentIngestionQueue` | `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/` | Phase 21 |
| `IGBrainMcpClient` | `src/Services/LeanKernel.Gateway/Memory/` | Phase 18/21 |
| `IPermit`, `IdentityContext` | `src/Common/LeanKernel.Core/` | Phase 20 |
| `IChannelMemoryPolicyResolver` | `src/Common/LeanKernel.Core/Interfaces/` | Phase 10/15 |
| Turn pipeline (Phase 03) | `src/Common/LeanKernel.Logic/TurnRuntime/` | Phase 03 |
| `EntityContext` + migrations | `src/Common/LeanKernel.Data/` | Phase 01+ |
| Phase 07 plan activities reference | `docs/plans/phase-07-learning-scheduler/activities.md` | Planning |
| Phase 21 Intelligent Brain Delta reference | `docs/plans/phase-21-channel-document-ingestion/index.md` lines 87–93, `docs/plans/phase-21-channel-document-ingestion/activities.md` lines 175–180 | Planning |
| `TurnEvent` / `TurnTelemetryEntity` | `src/Common/LeanKernel.Core/Events/TurnEvent.cs`, `src/Common/LeanKernel.Core/Entities/TurnTelemetryEntity.cs` | Phase 17 |

## Optional Inputs
- Source repository behavioral references: `~/source/repos/leankernel/src/LeanKernel.Learning/*.cs`, `LeanKernel.Scheduler/*.cs`, `LeanKernel.Context/Identity/*.cs`

## Input Validation Checklist
- [x] All required inputs in the current implementation worktree
- [ ] Phase 03 turn pipeline completion hook verified
- [ ] `FactExtractionService` operational and testable
