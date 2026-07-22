# Solution Structure

This page documents the projects that currently exist in this repository.

## Current Solution

The app-only solution file is [`../../src/LeanKernel.sln`](../../src/LeanKernel.sln).

The full repo solution, which also includes the test projects, is [`../../LeanKernel.sln`](../../LeanKernel.sln).

Projects in the app-only solution:

| Project | Role |
|---|---|
| `src/Common/LeanKernel.Core` | Shared entities and cross-project interfaces/contracts |
| `src/Terminals/LeanKernel.Channels.Common` | Shared terminal/gateway helpers (health response writer, gateway health probe, connection-string resolver, channel binding token resolver) |
| `src/Common/LeanKernel.Data` | EF Core context, migrations, interceptors, design-time factory |
| `src/Common/LeanKernel.Logic` | Chat history provider, memory pipeline, identity resolution, MAF-facing logic services |
| `src/Services/LeanKernel.Gateway` | Web host, endpoint mapping, auth/session middleware, GBrain wiring, agent session store |
| `src/Terminals/LeanKernel.Channels.Signal` | Signal channel terminal process (JSON-RPC socket transport to signal-cli sidecar) |
| `src/Terminals/LeanKernel.Channels.Teams` | Teams Bot Framework terminal process (webhook ingress + connector egress) |

Test projects:

- `test/LeanKernel.Tests.Unit`
- `test/LeanKernel.Tests.Integration`
- `test/LeanKernel.Tests.Playwright`

## Dependency Direction

The current direct project references are:

```mermaid
flowchart BT
    Gateway[LeanKernel.Gateway] --> Logic[LeanKernel.Logic]
    Gateway --> Data[LeanKernel.Data]
    Gateway --> Core[LeanKernel.Core]

    Logic --> Data
    Logic --> Core

    Signal[LeanKernel.Channels.Signal] --> Data
    Signal --> Logic
    Signal --> ChannelsCommon

    Teams[LeanKernel.Channels.Teams] --> Data
    Teams --> Logic
    Teams --> ChannelsCommon

    ChannelsCommon --> Data
    Data --> Core
```

These arrows reflect the current `.csproj` references in `src/` rather than a conceptual layering sketch.

- `Gateway` depends on `Logic`, `Data`, and `Core`
- `Logic` depends on `Data` and `Core`
- `Data` depends on `Core`
- `Channels.Common` depends on `Data`
- Channel terminals are edge processes; each terminal depends on `Logic`, `Data`, and `Channels.Common`, and does not reference `Gateway` directly
- Channel terminals reach `Core` transitively through `Data` and `Logic`; they do not reference `Core` directly in the current solution
- `Core` is the bottom layer
