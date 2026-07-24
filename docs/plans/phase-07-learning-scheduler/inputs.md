# Phase 07 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Memory pipeline + knowledge service | `src/Common/LeanKernel.Logic/Memory/*`, `src/Services/LeanKernel.Gateway/Providers/GBrainKnowledgeService.cs` | Rebuild maintainer |
| Turn-completion hook | Phase 03 pipeline event emission | Rebuild maintainer |
| Identity partitioning | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs`, `IdentityIsolationKeyProvider.cs` | Rebuild maintainer |
| Source learning pipeline | `~/source/repos/leankernel/src/LeanKernel.Learning/*` | Reviewer |
| Source onboarding intelligence | `~/source/repos/leankernel/src/LeanKernel.Context/Identity/*` | Reviewer |
| Source scheduler | `~/source/repos/leankernel/src/LeanKernel.Scheduler/*`, `src/LeanKernel.Persistence/Entities/ScheduledJobEntity.cs` | Reviewer |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` (for job/gap entities) | Rebuild maintainer |

## Optional Inputs
- Source PRDs: `phase-3-post-turn-learning-prd.md`, `phase-3-scheduled-jobs-proactive-tasks-prd.md`, `identity-intent-extraction-pipeline-prd.md`, `identity-onboarding` feature docs.

## Intelligent Brain Delta Inputs
- Native GBrain Dream run semantics and phase controls (`gbrain dream --json --phase ...`) from upstream implementation docs.
- GBrain/LiteLLM model routing defaults for Dream (`models.dream.*`) and runtime verification surfaces (`gbrain models`).
- Phase 21 enrichment trigger/event contracts and queue depth telemetry for Dream scheduling triggers.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Turn-completion hook available to feed the learning queue
- [ ] Dream orchestration prerequisites (source mapping, lock/retry semantics, model config defaults) are documented and testable
