# Phase 14 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Action risk model | Severity classification + autonomy levels | C# source |
| Policy engine | Per-person/tenant decision (approve/require/block) | C# source |
| Enforcement point | Single interception in the action/tool path | C# source |
| Approval workflow | Request/notify/confirm/deny/expire (default deny) | C# source |
| Action audit log | Durable, tamper-evident, person/tenant-scoped | C# + EF migration |
| Consumer integration | Hooks for Phases 11-13 with fail-safe behavior | C# source |
| Configuration + validation | Autonomy levels, overrides, timeouts, quiet hours | C# + appsettings |
| Tests | Classification, policy, lifecycle, no-bypass, audit coverage | xUnit projects |
| Documentation | Autonomy + approval + audit docs | Markdown |

## Optional Outputs
- Policy-authoring and audit-review surfaces for the Phase 09 admin UI.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
