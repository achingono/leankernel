# Solution Structure

This page documents the projects that currently exist in this repository.

## Current Solution

The app-only solution file is [`../../src/LeanKernel.sln`](../../src/LeanKernel.sln).

The full repo solution, which also includes the test projects, is [`../../LeanKernel.sln`](../../LeanKernel.sln).

Projects in the app-only solution:

| Project | Role |
|---|---|---|
| `src/Common/LeanKernel.Core` | Shared entities and cross-project interfaces/contracts, including canonical identity and event-envelope contracts |
| `src/Terminals/LeanKernel.Channels.Common` | Shared terminal configuration and gateway health check helpers |
| `src/Common/LeanKernel.Data` | EF Core context, migrations, interceptors, design-time factory |
| `src/Common/LeanKernel.Logic` | Chat history provider, memory pipeline, identity resolution, tool runtime, turn pipeline, telemetry, event spine, and MAF-facing logic services |
| `src/Services/LeanKernel.Services.Common` | Shared service-host plumbing: GBrain MCP/memory/document clients, health checks, DB provider options ext, and worker health state |
| `src/Services/LeanKernel.Services.Gateway` | Web host, endpoint mapping, auth/session middleware, attachment ingestion, and composition of logic and common services |
| `src/Services/LeanKernel.Services.Learning` | Background learning worker: turn-event processing, fact extraction, onboarding, cron scheduler, and dream cycle execution |
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
    Gateway[LeanKernel.Services.Gateway] --> Logic[LeanKernel.Logic]
    Gateway --> Data[LeanKernel.Data]
    Gateway --> Core[LeanKernel.Core]
    Gateway --> ServicesCommon[LeanKernel.Services.Common]

    Logic --> Data
    Logic --> Core

    Learning[LeanKernel.Services.Learning] --> Logic
    Learning --> Data
    Learning --> ServicesCommon

    ServicesCommon --> Logic
    ServicesCommon --> Data

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
