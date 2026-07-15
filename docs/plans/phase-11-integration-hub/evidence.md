# Phase 11 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Tool governance | `src/Common/LeanKernel.Logic/Tools/ToolGovernancePolicy.cs`, `ToolRegistry.cs` | Governed tool exposure |
| Egress validation | `src/Services/LeanKernel.Gateway/Tools/Dynamic/EgressValidator.cs` | Outbound call safety |
| Secret handling | `src/Services/LeanKernel.Gateway/Tools/Dynamic/DynamicSkillTool.cs:179-205` | Secret resolution pattern |
| Person-scoped identity | `docs/plans/phase-10-cross-channel-memory/index.md` | Credentials follow the person |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Vault entity + migration home |
| Auth wiring | `src/Services/LeanKernel.Gateway/Programs.cs` | OIDC/JWT integration |
