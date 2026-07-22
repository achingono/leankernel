# LeanKernel Documentation

This documentation set covers the current gateway-centered LeanKernel service stack in this repository.

It follows these conventions:

- kebab-case file names
- hierarchical folders by domain
- `index.md` in each folder
- one canonical page per topic

## Start Paths

- New to the repo: [`getting-started/index.md`](getting-started/index.md)
- System design and boundaries: [`architecture/index.md`](architecture/index.md)
- Runtime capabilities: [`features/index.md`](features/index.md)
- HTTP surface: [`api/index.md`](api/index.md)
- Runtime configuration: [`configuration/index.md`](configuration/index.md)
- Build and test workflows: [`development/index.md`](development/index.md)
- Local stack operations: [`operations/index.md`](operations/index.md)
- Architectural decisions: [`decisions/index.md`](decisions/index.md)
- Planning artifacts: [`plans/`](plans/)

## Runtime Summary

```mermaid
flowchart LR
    Client[Client / API Caller] --> Gateway[LeanKernel.Gateway]
    Gateway --> Agent[Named AIAgent]
    Agent --> History[DbChatHistoryProvider]
    Agent --> Memory[MemoryProvider]
    Agent --> Tools[Tool runtime / MCP Webwright]
    History --> Data[(EntityContext)]
    Memory --> GBrain[GBrain MCP]
    Agent --> State[DbAgentStateStore]
    State --> Data
    Agent --> LiteLLM[LiteLLM / OpenAI-compatible model endpoint]
```

## Current Scope

This docs set describes the implementation that actually exists today:

- `src/Common/LeanKernel.Core`
- `src/Common/LeanKernel.Data`
- `src/Common/LeanKernel.Logic`
- `src/Services/LeanKernel.Gateway`
- `src/Terminals/LeanKernel.Channels.Common`
- `src/Terminals/LeanKernel.Channels.Signal`
- `src/Terminals/LeanKernel.Channels.Teams`
- test projects under `test/`

## Code Anchors

- Gateway composition root: [`../src/Services/LeanKernel.Gateway/Program.cs`](../src/Services/LeanKernel.Gateway/Program.cs)
- Solution file: [`../src/LeanKernel.sln`](../src/LeanKernel.sln)
- Local stack: [`../docker-compose.yml`](../docker-compose.yml)
- ADRs: [`decisions/index.md`](decisions/index.md)
