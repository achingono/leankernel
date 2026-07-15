# Phase 15 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Identity resolver + guest/channel logic | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs` (`ResolveGuestUserAsync` :102-144, `ResolveOrCreateChannelAsync` :147-184) | Rebuild maintainer |
| User identity model | `src/Common/LeanKernel.Core/Entities/{UserEntity,ChannelEntity}.cs` (`Issuer`/`Subject`) | Rebuild maintainer |
| Permit + isolation | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs`, `src/Services/LeanKernel.Gateway/Providers/{RequestContextPermit,IdentityIsolationKeyProvider}.cs` | Rebuild maintainer |
| Channel transport (sender id source) | `docs/plans/phase-06-channels/` | Rebuild maintainer |
| Person model / linking | `docs/plans/phase-10-cross-channel-memory/` | Rebuild maintainer |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |

## Optional Inputs
- Source channel/identity references: `~/source/repos/leankernel/src/LeanKernel.Channels/ChannelAuthenticator.cs`, `docs/features/identity-onboarding.md`.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Identifier normalization rules (E.164, email) decided
