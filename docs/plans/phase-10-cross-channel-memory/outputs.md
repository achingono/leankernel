# Phase 10 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Person identity model | Person entity + channel-identity mapping | C# + EF migration |
| Identity resolution update | Resolves personId + channelId per request | C# source |
| Identity linking flow | Verified link/unlink (one-time code) | C# source |
| Person-scoped memory | `MemoryScope` + `GBrainMemoryClient` keyed by personId | C# source |
| Preference profile | Person-level preferences surfaced to context | C# source |
| Permit and memory-scope update | `IPermit` adds person context; memory stays person-scoped without rekeying agent-session isolation | C# source |
| Memory migration | Reversible, dry-run-capable channel->person key migration | C# + script |
| Configuration + validation | Scope mode, history-sharing, linking settings | C# + appsettings |
| Tests | Cross-channel sharing, isolation, tenant, migration coverage | xUnit projects |
| Documentation | Identity-partitioning + memory-pipeline docs updated | Markdown |

## Optional Outputs
- Linking UI surface reserved for Phase 09/onboarding.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
