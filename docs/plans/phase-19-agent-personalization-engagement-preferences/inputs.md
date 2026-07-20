# Phase 19 Agent Personalization and Engagement Preferences — Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Current prompt composition path (flat system instructions) | `src/Common/LeanKernel.Logic/TurnRuntime/PromptAssembler.cs`, `src/Common/LeanKernel.Logic/Configuration/AgentSettings.cs` | Runtime maintainer |
| Admission/budget stage that orders `system`/`identity` context first | `src/Common/LeanKernel.Logic/TurnRuntime/ContextGatekeeper.cs` | Runtime maintainer |
| Turn state carrier and `ContextItem` shape (Source/Content/Metadata) | `src/Common/LeanKernel.Logic/TurnRuntime/TurnContext.cs` | Runtime maintainer |
| Identity partitioning keys (tenant/person/user/channel) | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs`, `src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs` | Identity maintainer |
| EF Core context + migration workflow for new entities | `src/Common/LeanKernel.Data/EntityContext.cs`, `src/Common/LeanKernel.Data/Migrations/` | Data maintainer |
| Reference pattern: allowlisted, deterministic prompt rendering | `docs/plans/phase-16-identity-claims-context/` (identity-context assembler) | Runtime maintainer |
| Configuration shape to preserve (`Agents` section) | `appsettings*.json`, `AgentSettings` | Gateway maintainer |
| Tenant/channel-scoped policy entity precedent | `src/Common/LeanKernel.Core/Entities/ChannelMemoryPolicyEntity.cs`, `TenantEntity.cs`, `UserEntity.cs` | Data maintainer |

## Optional Inputs
- Existing memory-pipeline scoping conventions (`docs/features/memory-pipeline.md`, `docs/features/identity-partitioning.md`) for consistent scope-key handling.
- Diagnostics/admission-trace conventions for surfacing merge decisions in telemetry.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] `IPermit` exposes all four partition keys used by the preference key
- [ ] Prompt composition still terminates with the current user message last
