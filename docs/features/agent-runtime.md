# Agent Runtime

The current agent runtime is hosted through Microsoft Agent Framework inside `LeanKernel.Gateway`.

## What Exists Today

- named `AIAgent` registration in the gateway composition root
- OpenAI-compatible responses and conversations endpoint mapping
- EF-backed transcript history provider
- EF-backed durable agent state store
- GBrain-backed memory context provider

Key code anchors:

- [`../../src/Services/LeanKernel.Gateway/Programs.cs`](../../src/Services/LeanKernel.Gateway/Programs.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs)
- [`../../src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs`](../../src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs)

## What This Runtime Is Not Yet

The rebuild does not yet expose the broader multi-service and UI footprint described in the older aspirational README. The implemented runtime is currently centered on the gateway plus the common libraries.
