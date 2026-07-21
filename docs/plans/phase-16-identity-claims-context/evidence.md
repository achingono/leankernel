# Phase 16 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Identity profile entity | `src/Common/LeanKernel.Core/Entities/UserEntity.cs` | Added `RolesJson`, `GroupsJson`, `CustomClaimsJson`, `PreferredUserName`, `Locale`, `TimeZone`, `Organization` |
| Identity profile migration | `src/Common/LeanKernel.Data/Migrations/20260719163620_AddIdentityClaimsContext.cs` | Adds all new columns to `Users` table |
| Profile configuration | `src/Common/LeanKernel.Logic/Configuration/IdentityClaimsContextSettings.cs` | Claim allowlist, prompt fields, caps, enable/disable |
| Profile validation | `src/Services/LeanKernel.Gateway/Programs.cs:89-96` | `ValidateOnStart()` for non-negative bounds and `MaxPromptTokens > 0` |
| Claims capture + refresh | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:529-634` | `ApplyIdentityProfile` captures allowlisted claims; called on create and re-resolution |
| Claim constants | `src/Common/LeanKernel.Core/Constants.cs` | OIDC/OAuth claim type constants replacing magic strings |
| Identity context assembler | `src/Common/LeanKernel.Logic/Providers/IdentityContextAssembler.cs` | Renders persisted profile into deterministic prompt block |
| Context injection point | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs:38-66` | Injects identity context via `IdentityContextAssembler.Build(user)` |
| Context gatekeeper admission | `src/Common/LeanKernel.Logic/TurnRuntime/ContextGatekeeper.cs:33` | `ContextSource.Identity` admitted under system budget |
| Prompt assembly ordering | `src/Common/LeanKernel.Logic/TurnRuntime/PromptAssembler.cs:35-42` | Identity context ordered after system, before memory/retrieval |
| Appsettings | `src/Services/LeanKernel.Gateway/appsettings.json` | `Identity:ClaimsContext` section with defaults |
| Tests | `test/LeanKernel.Tests.Unit/Identity/IdentityResolverTests.cs:585-647` | `ResolveOrCreateUserAsync_WithClaimsContextEnabled_PersistsProfile`, `ResolveOrCreateUserAsync_RefreshOnReResolve` |
| Tests | `test/LeanKernel.Tests.Unit/Providers/IdentityContextAssemblerTests.cs` | Assembler rendering, empty/null handling, truncation |
| Tests | `test/LeanKernel.Tests.Unit/Providers/MemoryProviderBehaviorTests.cs` | Identity context integration in memory provider |
| Documentation | `docs/features/identity-partitioning.md` | Identity Claims Context section |
| Documentation | `docs/configuration/appsettings-reference.md` | `Identity:ClaimsContext` key reference |
