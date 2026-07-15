# Phase 15 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Channel-identity directory | `(tenant, channelType, normalizedId) -> userId` entity | C# + EF migration |
| Identifier normalization | E.164 / email normalization on write + lookup | C# source |
| Channel resolution | `IdentityResolver` maps senders to known users via `Issuer`/`Subject` | C# source |
| Unknown-sender policy | Known-only / auto-provision / guest fallback (configurable) | C# source |
| Provisioning + claim | Admin pre-provisioning + first-contact verification | C# source |
| Person integration | Resolved channel user maps to canonical person (Phase 10) | C# source |
| Configuration + validation | Normalization, policy, provisioning settings | C# + appsettings |
| Tests | Normalization, resolution, provisioning, isolation coverage | xUnit projects |
| Documentation | Channel identity mapping docs | Markdown |

## Optional Outputs
- Admin surface for managing channel-identity mappings (Phase 09).

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
