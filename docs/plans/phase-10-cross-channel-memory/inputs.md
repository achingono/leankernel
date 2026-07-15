# Phase 10 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Memory scope + client | `src/Common/LeanKernel.Logic/Memory/MemoryModels.cs`, `src/Services/LeanKernel.Gateway/Providers/GBrainMemoryClient.cs` (BuildScopedSlug/Namespace) | Rebuild maintainer |
| Memory pipeline | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` | Rebuild maintainer |
| Identity resolution | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs` | Rebuild maintainer |
| Isolation + permit | `src/Services/LeanKernel.Gateway/Providers/{IdentityIsolationKeyProvider,RequestContextPermit}.cs`, `src/Common/LeanKernel.Core/Interfaces/IPermit.cs` | Rebuild maintainer |
| Identity entities | `src/Common/LeanKernel.Core/Entities/{UserEntity,ChannelEntity,TenantEntity}.cs`, `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |
| Identity-partitioning feature doc | `docs/features/identity-partitioning.md`, `docs/features/memory-pipeline.md` | Reviewer |
| Channels context | `docs/plans/phase-06-channels/` | Rebuild maintainer |

## Optional Inputs
- Source onboarding/identity references: `~/source/repos/leankernel/src/LeanKernel.Context/Identity/*`.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Current memory-key format confirmed against `GBrainMemoryClient`
