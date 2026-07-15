# Phase 06 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Identity partitioning + channel concept | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs`, `src/Common/LeanKernel.Core/Entities/ChannelEntity.cs` | Rebuild maintainer |
| Tenant + user resolution primitives | `IdentityResolver.ResolveTenantAsync`/`ResolveOrCreateUserAsync`, `src/Common/LeanKernel.Core/Entities/{TenantEntity,UserEntity}.cs` | Rebuild maintainer |
| HTTP identity resolution reference | `src/Services/LeanKernel.Gateway/Providers/{TenantResolutionMiddleware,RequestContextPermit}.cs` | Rebuild maintainer |
| Gateway endpoint + auth | `src/Services/LeanKernel.Gateway/Programs.cs` (`app.MapOpenAIResponses()`), `Identity.Token`/`OpenId` settings | Rebuild maintainer |
| Terminals home | `src/Terminals/` (placeholder for client/edge terminal projects) | Rebuild maintainer |
| Teams integration surface | Microsoft Teams Bot Framework SDK / Azure Bot registration | Repository owner |
| Turn runtime entry point | Phase 03 pipeline / current gateway turn path | Rebuild maintainer |
| Source channel abstraction | `~/source/repos/leankernel/src/LeanKernel.Channels/{ChannelHostedService,ChannelRouter,ChannelAuthenticator}.cs` | Reviewer |
| Source Signal adapter | `~/source/repos/leankernel/src/LeanKernel.Channels/{SignalChannel,TypingIndicatorKeepAlive,SignalAttachmentParser}.cs` | Reviewer |
| Channel docs | `~/source/repos/leankernel/docs/features/channels/`, `channel-routing.md` | Reviewer |
| Config shape | `docs/configuration/index.md`, AGENTS.md config-shape rules | Repository owner |
| Memory scope + client (policy consumer) | `src/Common/LeanKernel.Logic/Providers/IMemoryClient.cs`, `src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs` (BuildScopedSlug/Namespace) | Rebuild maintainer |
| Cross-channel memory phase | `docs/plans/phase-10-cross-channel-memory/` (enforces the policy defined here) | Rebuild maintainer |

## Optional Inputs
- Source `channel-consolidation.md` and typing-indicator keepalive PRD for acceptance detail.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Signal daemon topology and account configuration confirmed
- [ ] Channel→tenant scope mapping source confirmed (which account maps to which tenant)
