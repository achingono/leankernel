# System Overview

LeanKernel runs as a `.NET 10` modular monolith with Gateway as the HTTP + UI host.

## Runtime Topology

```mermaid
flowchart LR
    Client[API/UI Client] --> Gateway[LeanKernel.Gateway]
    Gateway --> Agents[LeanKernel.Agents]
    Agents --> Context[LeanKernel.Context]
    Agents --> Knowledge[LeanKernel.Knowledge]
    Agents --> Persistence[LeanKernel.Persistence]
    Agents --> Tools[LeanKernel.Tools]
    Agents --> Diagnostics[LeanKernel.Diagnostics]
```

## Related Pages

- [Architecture index](index.md)
- [Solution structure](solution-structure.md)
- [Runtime flows](runtime-flows.md)
