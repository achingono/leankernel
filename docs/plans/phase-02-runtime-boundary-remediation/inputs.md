# Phase 02 Inputs

## Required Inputs
_List every document, data source, or artefact that must exist before this phase can start._

| Input | Source | Owner |
|---|---|---|
| Layer-by-layer review findings dated 2026-07-13 | OpenCode review conversation in this worktree session | OpenCode |
| Identity partitioning feature doc | `docs/features/identity-partitioning.md` | repository owner |
| ADR 0002 persisted identity partitioning | `docs/decisions/0002-partition-runtime-state-by-persisted-identities.md` | repository owner |
| ADR 0003 transcript vs runtime separation | `docs/decisions/0003-separate-transcript-sessions-from-agent-runtime-state.md` | repository owner |
| Current solution structure | `docs/architecture/solution-structure.md` | repository owner |
| Current runtime implementation | `src/Services/LeanKernel.Gateway`, `src/Common/LeanKernel.Logic`, `src/Common/LeanKernel.Data`, `src/Common/LeanKernel.Core` | repository owner |

## Optional Inputs
_Inputs that improve the phase but are not blockers._
- Existing production expectations for anonymous access to `/v1/responses` and `/v1/conversations`.
- Operational expectations for transcript retention, compaction thresholds, and memory write deduplication.
- Reverse-proxy deployment details that define the trusted forwarded-header boundary.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Reviewer confirms the plan aligns with ADR 0002 and ADR 0003 before implementation starts
