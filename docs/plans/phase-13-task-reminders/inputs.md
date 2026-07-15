# Phase 13 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Scheduler | `docs/plans/phase-07-learning-scheduler/` (cron/job executor/time boundary) | Rebuild maintainer |
| Channels + delivery | `docs/plans/phase-06-channels/` | Rebuild maintainer |
| Person-scoped identity/memory | `docs/plans/phase-10-cross-channel-memory/` | Rebuild maintainer |
| Learning capture hook | `docs/plans/phase-07-learning-scheduler/` (follow-up/gap steps) | Rebuild maintainer |
| Tool runtime + governance | `src/Common/LeanKernel.Logic/Tools/*` | Rebuild maintainer |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |

## Optional Inputs
- Preference profile (Phase 10) for delivery-channel and quiet-hours settings.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Natural-language time-parsing approach decided
