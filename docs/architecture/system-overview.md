# System Overview

The current LeanKernel rebuild is a .NET 10 gateway-centered microservice architecture built around Microsoft Agent Framework (MAF) extension points.

## Core Topology

- `LeanKernel.Gateway` hosts the HTTP runtime.
- `LeanKernel.Logic` supplies reusable runtime services and MAF providers.
- `LeanKernel.Data` owns EF Core persistence.
- `LeanKernel.Core` holds shared entities and contracts.

The composition root is [`../../src/Services/LeanKernel.Gateway/Programs.cs`](../../src/Services/LeanKernel.Gateway/Programs.cs).

## Main Runtime Components

| Component | Responsibility | Code anchor |
|---|---|---|
| Gateway host | DI, auth, session middleware, endpoint mapping, startup migrations | `src/Services/LeanKernel.Gateway/Programs.cs` |
| Request permit | Resolve tenant, user, channel, and guest fallback for the current request | `src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs` |
| Agent session store | Persist MAF session state blobs | `src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs` |
| Chat history provider | Persist and retrieve transcript turns through EF Core | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs` |
| Memory provider | Retrieve memory context and persist normalized facts | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` |
| Memory backend | GBrain-backed `IMemoryClient` implementation | `src/Services/LeanKernel.Gateway/Providers/GBrainMemoryClient.cs` |
| MCP browser tools | Webwright MCP discovery, tool adapter registration, and per-call invocation | `src/Common/LeanKernel.Logic/Mcp/` |

## Major Design Choices

- MAF-native runtime instead of a custom agent framework
- persisted identity partitioning by tenant, user, and channel
- separate transcript session persistence and agent state persistence
- deterministic-first memory shaping with bounded model-assisted refinement
- browser automation is provided by pre-configured Webwright MCP tools, not a custom Playwright sidecar
- MCP tool adapters use LeanKernel-owned registration and invocation boundaries instead of reusing stale discovery clients

Those decisions are also captured in the repo ADRs under [`../decisions/index.md`](../decisions/index.md).
