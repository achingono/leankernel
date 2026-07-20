# Phase 16 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Durable identity profile fields | `src/Common/LeanKernel.Core/Entities/UserEntity.cs` | Added preferred username, locale, timezone, organization, roles/groups JSON, custom claims JSON |
| Persistence model update | `src/Common/LeanKernel.Data/EntityContext.cs` | Added mappings for identity profile columns |
| Schema migration | `src/Common/LeanKernel.Data/Migrations/20260719163620_AddIdentityClaimsContext.cs` | Adds identity-claims context columns to `Users` |
| Refresh-on-resolve claim capture | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs` | Applies allowlisted claim profile for existing and newly created authenticated users |
| Deterministic identity rendering | `src/Common/LeanKernel.Logic/Providers/IdentityContextAssembler.cs` | Renders ordered prompt fields and bounded identity profile block |
| Per-turn context injection | `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` | Injects assembled identity profile into AI context each turn |
| Config and startup validation | `src/Common/LeanKernel.Logic/Configuration/IdentityClaimsContextSettings.cs` | Controls enablement, allowlists, render fields, and bounds |
| Gateway binding and validation | `src/Services/LeanKernel.Gateway/Programs.cs` | Binds `Identity:ClaimsContext`, validates bounds with `ValidateOnStart()` |
| Default runtime config | `src/Services/LeanKernel.Gateway/appsettings.json` | Provides default `Identity:ClaimsContext` values and `custom_claims` in `PromptFields` |
| Unit test coverage | `test/LeanKernel.Tests.Unit/Identity/IdentityResolverTests.cs` | Verifies persistence/refresh, allowlist, truncation limits |
| Unit test coverage | `test/LeanKernel.Tests.Unit/Providers/IdentityContextAssemblerTests.cs` | Verifies deterministic output, field filtering, and token-budget truncation |
| Unit test coverage | `test/LeanKernel.Tests.Unit/Providers/MemoryProviderBehaviorTests.cs` | Verifies identity context injection path and behavior |
| Quality verification | `scripts/quality/sonarqube-scan.sh` | Quality gate passed after hotspot review and new-code baseline reset |
| Context gatekeeper (future) | `docs/plans/phase-03-turn-runtime/index.md` | Budgeted admission of identity context |
