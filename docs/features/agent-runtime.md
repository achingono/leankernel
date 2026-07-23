# Agent Runtime

The current agent runtime is hosted through Microsoft Agent Framework inside `LeanKernel.Gateway`.

## What Exists Today

- named `AIAgent` registration in the gateway composition root
- OpenAI-compatible responses and conversations endpoint mapping
- EF-backed transcript history provider
- EF-backed durable agent state store
- GBrain-backed memory context provider
- Document upload and attachment-ingestion integration with asynchronous queueing

Key code anchors:

- [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs)
- [`../../src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs`](../../src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs)

## Runtime Boundary

The runtime is intentionally gateway-centric. Channel terminals (`Signal`, `Teams`) and companion services (`LiteLLM`, `GBrain`, `Webwright`) operate as external edge/infra components while core orchestration remains in `LeanKernel.Gateway` and `LeanKernel.Logic`.
