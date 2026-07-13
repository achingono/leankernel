# Solution Structure

This page documents the projects that actually exist in the current rebuild.

## Current Solution

The active solution file is [`../../src/LeanKernel.sln`](../../src/LeanKernel.sln).

Projects in the solution:

| Project | Role |
|---|---|
| `src/Common/LeanKernel.Core` | Shared entities and cross-project interfaces/contracts |
| `src/Common/LeanKernel.Data` | EF Core context, migrations, interceptors, design-time factory |
| `src/Common/LeanKernel.Logic` | Chat history provider, memory pipeline, identity resolution, MAF-facing logic services |
| `src/Services/LeanKernel.Gateway` | Web host, endpoint mapping, auth/session middleware, GBrain wiring, agent session store |

Test projects:

- `test/LeanKernel.Tests.Unit`
- `test/LeanKernel.Tests.Integration`
- `test/LeanKernel.Tests.Playwright`

## Dependency Direction

- `Gateway` depends on `Logic`, `Data`, and `Core`
- `Logic` depends on `Data` and `Core`
- `Data` depends on `Core`
- `Core` is the bottom layer

## Current Non-Structure

The repository README still lists additional services and terminals that are not present in this rebuild yet. Those should be treated as roadmap intent, not as implemented modules.

Reference: [`../../README.md`](../../README.md)
