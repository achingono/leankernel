# Phase 16 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Identity profile store | Extended `UserEntity`/related claims entity + migration | C# + EF migration |
| Claim allowlist | Config-driven approved claim set | C# + appsettings |
| Extended claim capture | `IdentityResolver`/`ClaimsPrincipalExtensions` read allowlisted claims | C# source |
| Persist + refresh | Identity profile updated on each login with change detection | C# source |
| Identity-context assembler | Deterministic system-prompt identity block | C# source |
| Context integration | Injection via `MemoryProvider` / Phase 03 gatekeeper under budget | C# source |
| Configuration + validation | Allowlist, prompt fields, enable/disable | C# + appsettings |
| Tests | Persistence/refresh, allowlist, rendering, budget, isolation coverage | xUnit projects |
| Documentation | Identity-to-context flow docs | Markdown |

## Optional Outputs
- Promotion hook to feed the Phase 10 person-level profile.

## Output Quality Checklist
- [x] All mandatory outputs produced
- [x] All outputs reviewed before gate
- [x] Evidence log updated with output references
