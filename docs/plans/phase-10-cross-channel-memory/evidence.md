# Phase 10 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Current memory key format | `src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs` (BuildScopedSlug/BuildScopedNamespace) | `memory/{tenantId}/{userId}/{channelId}/{key}` — channel-scoped today; becomes `memory/{tenantId}/{personId}/{channelId}/{key}` with policy-driven reads |
| Current isolation key | `src/Services/LeanKernel.Gateway/Providers/IdentityIsolationKeyProvider.cs:18-29` | `TenantId|ChannelId|UserId(|SessionId)` on the agent-session path, distinct from memory transport |
| Permit contract | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs` | Exposes UserId/TenantId/ChannelId; needs personId |
| Identity resolution | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs` | Per-channel identity creation |
| Memory pipeline | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` | Consumes `MemoryScope` |
| Fact key + supersession | `src/Common/LeanKernel.Logic/Memory/MemoryPageKeyBuilder.cs` (`facts/{dim}/{subject-slug}/{factId}`), `MemoryModels.cs` (`SupersededBy`, `Supersedes`/`ExplicitLinks`, `IsRetired`) | Basis for cross-channel 5W1H reconciliation |
| Dimension + normalization | `src/Common/LeanKernel.Logic/Memory/{MemoryPageNormalizer,MemoryDimensionClassifier,MemoryPageFields}.cs` | 5W1H fields must converge across shared channels |
| Channel sharing policy | `docs/plans/phase-06-channels/` | Defines the `Share`/`Access` policy + resolution contract enforced here |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Entities + migrations home |
