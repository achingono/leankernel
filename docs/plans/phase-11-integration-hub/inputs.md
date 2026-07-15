# Phase 11 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Tool runtime + governance | `src/Common/LeanKernel.Logic/Tools/*`, `src/Services/LeanKernel.Gateway/Tools/*` | Rebuild maintainer |
| Egress validation | `src/Services/LeanKernel.Gateway/Tools/Dynamic/EgressValidator.cs` | Rebuild maintainer |
| Secret resolution pattern | `src/Services/LeanKernel.Gateway/Tools/Dynamic/DynamicSkillTool.cs` (bearer/secret handling) | Rebuild maintainer |
| Person-scoped identity | `docs/plans/phase-10-cross-channel-memory/` | Rebuild maintainer |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |
| Auth wiring | `src/Services/LeanKernel.Gateway/Programs.cs` (JWT/OIDC) | Rebuild maintainer |

## Optional Inputs
- Provider API docs for the chosen reference connector (Google/Microsoft Graph/GitHub).
- Phase 14 autonomy/approval design (to gate writes) if available.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Encryption-at-rest mechanism for tokens decided and available
