# Solution Structure

This page documents the projects that actually exist in the current rebuild.

## Current Solution

The active solution file is [`../../src/LeanKernel.sln`](../../src/LeanKernel.sln).

Projects in the solution:

| Project | Role |
|---|---|
| `src/Common/LeanKernel.Core` | Shared entities and cross-project interfaces/contracts |
| `src/Common/LeanKernel.Channels.Common` | Shared terminal/gateway helpers (health response writer, gateway health probe, connection-string resolver, channel binding token resolver) |
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

- `Gateway` depends on `Logic`, `Data`, `Core`, and `Channels.Common`
- `Logic` depends on `Data` and `Core`
- `Data` depends on `Core`
- Channel terminals are edge processes; they depend on `Data`, `Core`, and `Channels.Common`, and do not depend on `Gateway`/`Logic`
- `Core` is the bottom layer
