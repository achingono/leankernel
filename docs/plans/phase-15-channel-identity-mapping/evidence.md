# Phase 15 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Current guest fallback | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:102-144` | Unknown senders become `anonymous` guests today |
| Channel resolution | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:147-184` | `ResolveOrCreateChannelAsync` |
| User identity model | `src/Common/LeanKernel.Core/Entities/UserEntity.cs` | `Issuer`/`Subject` used to represent channel identities |
| Isolation/permit | `src/Services/LeanKernel.Gateway/Providers/IdentityIsolationKeyProvider.cs`, `src/Common/LeanKernel.Core/Interfaces/IPermit.cs` | Reflects resolved user |
| Channel transport | `docs/plans/phase-06-channels/index.md` | Supplies inbound sender identifier |
| Person model | `docs/plans/phase-10-cross-channel-memory/index.md` | Channel user -> canonical person |
