# Documentation Refresh Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Current gateway endpoint mappings | `src/Services/LeanKernel.Gateway/Program.cs` and endpoint extension files | OpenCode |
| Current feature implementation surface | `src/Common/*`, `src/Services/*`, `src/Terminals/*` | OpenCode |
| Current configuration model | `appsettings*.json` + `Configuration/*Settings.cs` | OpenCode |
| Current quality workflows | `scripts/quality/*.sh` | OpenCode |
| Existing docs content | `README.md`, `docs/**/*.md` | OpenCode |

## Optional Inputs
- Recent plan/evidence documents under `docs/plans/` for implementation context.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
