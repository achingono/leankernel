# Appsettings Reference

The gateway reads configuration primarily from:

- [`../../src/Services/LeanKernel.Gateway/appsettings.json`](../../src/Services/LeanKernel.Gateway/appsettings.json)
- [`../../src/Services/LeanKernel.Gateway/appsettings.Development.json`](../../src/Services/LeanKernel.Gateway/appsettings.Development.json)

## Current Sections

| Section | Purpose |
|---|---|
| `ConnectionStrings` | Database provider inputs. Local defaults use SQLite. |
| `OpenAI` | Base model endpoint, API key, default model, memory model settings, fact extraction model settings. |
| `Agents` | Default agent metadata and root path. |
| `Identity` | Anonymous user defaults plus token/OpenID settings. |
| `Files` | Root data path. |
| `Cors` | Local policy settings. |
| `GBrain` | MCP base URL, auth token, timeout. |

## OpenAI Subsections

`OpenAI` currently includes two important nested model configs:

- `Memory`
- `FactExtraction`

These are used by the logic-layer memory pipeline.

Code anchors:

- [`../../src/Common/LeanKernel.Logic/Configuration/MemorySettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/MemorySettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/FactExtractionSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/FactExtractionSettings.cs)

## Provider Selection Notes

Database provider selection is not hardcoded to one backend. The gateway resolves the first configured supported connection string in this order:

1. `Postgres`
2. `SqlServer`
3. `Sqlite`

Reference: [`../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs`](../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs)
