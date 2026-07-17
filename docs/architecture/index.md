# Architecture

System design and ownership boundaries for the current runtime.

## Canonical Pages

| Document | Description |
| ---------- | ------------- |
| [system-overview.md](system-overview.md) | Runtime topology and major boundaries. |
| [solution-structure.md](solution-structure.md) | Project ownership and dependency rules. |
| [runtime-flows.md](runtime-flows.md) | Request, session, and memory flow summary. |
| [data-and-persistence.md](data-and-persistence.md) | Entities, session state, and storage model. |

## Quick Reference

```mermaid
flowchart LR
    Client[API client] --> Gateway[LeanKernel.Gateway]
    Gateway --> Agent[MAF-hosted leankernel agent]
    Gateway --> Permit[RequestContextPermit]
    Agent --> History[DbChatHistoryProvider]
    Agent --> Memory[MemoryProvider]
    Agent --> Tools[Tool runtime and MCP adapters]
    Agent --> SessionStore[DbAgentStateStore]
    History --> Db[(EntityContext)]
    SessionStore --> Db
    Memory --> GBrain[(GBrain memory)]
    Agent --> Model[OpenAI-compatible model]
    Signal[Signal terminal] --> Shared[Channels.Common]
    Teams[Teams terminal] --> Shared
    Signal --> Gateway
    Teams --> Gateway
    Shared --> Db
```

Detailed diagrams live on the architecture detail pages:

- [system-overview.md](system-overview.md) for runtime topology
- [solution-structure.md](solution-structure.md) for direct project dependencies
- [runtime-flows.md](runtime-flows.md) for request and persistence flow

## Related Pages

- [Docs home](../index.md)
- [Features](../features/index.md)
- [Gateway API](../api/gateway-api.md)
