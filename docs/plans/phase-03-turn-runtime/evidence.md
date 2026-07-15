# Phase 03 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Source turn pipeline | `~/source/repos/leankernel/src/LeanKernel.Agents/TurnPipeline.cs` | Behavioral reference |
| Source context gating | `~/source/repos/leankernel/src/LeanKernel.Context/ContextGatekeeper.cs` | Deny-by-default reference |
| Source history shaping | `~/source/repos/leankernel/src/LeanKernel.Context/History/HistoryShaper.cs` | Compaction reference |
| Source continuation | `~/source/repos/leankernel/src/LeanKernel.Agents/ContinuationTurnPipeline.cs` | Long-running reference |
| Rebuild integration points | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`, `DbChatHistoryProvider.cs` | Current turn flow |
