# Phase 16 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Current claim capture | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:57-72` | Persists Email/First/Last/FullName/UserName only on create |
| Create-only persistence | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:50-55` | Existing users only bump `LastActivity`; profile not refreshed |
| Claim helpers | `src/Services/LeanKernel.Gateway/Extensions/ClaimsPrincipalExtensions.cs` | Name/Email/Id readers to extend |
| User entity | `src/Common/LeanKernel.Core/Entities/UserEntity.cs` | Profile fields + Issuer/Subject |
| Prompt injection point | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` | Where identity context would be injected today |
| Context gatekeeper (future) | `docs/plans/phase-03-turn-runtime/index.md` | Budgeted admission of identity context |
