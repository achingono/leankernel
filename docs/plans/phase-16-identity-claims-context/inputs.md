# Phase 16 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Claims-based user resolution | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:29-99` (`ResolveOrCreateUserAsync`) | Rebuild maintainer |
| Claim-reading helpers | `src/Services/LeanKernel.Gateway/Extensions/ClaimsPrincipalExtensions.cs` | Rebuild maintainer |
| User identity entity | `src/Common/LeanKernel.Core/Entities/UserEntity.cs` (Email/First/Last/FullName/Issuer/Subject) | Rebuild maintainer |
| Context/prompt injection point | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` (or Phase 03 gatekeeper) | Rebuild maintainer |
| Auth wiring | `src/Services/LeanKernel.Gateway/Programs.cs` (JWT/OIDC) | Rebuild maintainer |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |

## Optional Inputs
- Phase 03 context gatekeeper/budget design (`docs/plans/phase-03-turn-runtime/`).
- Phase 10 person profile (`docs/plans/phase-10-cross-channel-memory/`) for later promotion.

## Input Validation Checklist
- [x] All required inputs are current (not from a superseded version)
- [x] No required input is missing or in draft state
- [x] Claim allowlist (which claims to persist/render) decided
