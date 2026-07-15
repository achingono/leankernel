# Phase 10 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Current memory key format | `src/Services/LeanKernel.Gateway/Providers/GBrainMemoryClient.cs:86-100` | `memory/{tenantId}/{userId}/{channelId}/{key}` — channel-scoped today |
| Current isolation key | `src/Services/LeanKernel.Gateway/Providers/IdentityIsolationKeyProvider.cs:18-29` | `TenantId|ChannelId|UserId(|SessionId)` on the agent-session path, distinct from memory transport |
| Permit contract | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs` | Exposes UserId/TenantId/ChannelId; needs personId |
| Identity resolution | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs` | Per-channel identity creation |
| Memory pipeline | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` | Consumes `MemoryScope` |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Entities + migrations home |
