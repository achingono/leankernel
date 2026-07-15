# Phase 12 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Connector hub | `docs/plans/phase-11-integration-hub/`, connector registry + credential vault | Rebuild maintainer |
| Person-scoped identity/memory | `docs/plans/phase-10-cross-channel-memory/` | Rebuild maintainer |
| Tool runtime + governance | `src/Common/LeanKernel.Logic/Tools/*`, `src/Services/LeanKernel.Gateway/Tools/*` | Rebuild maintainer |
| Approval engine (or fallback) | `docs/plans/phase-14-autonomy-approvals/` | Rebuild maintainer |
| Scheduler + channels (for briefings) | `docs/plans/phase-07-learning-scheduler/`, `docs/plans/phase-06-channels/` | Rebuild maintainer |
| Provider API docs | Google Workspace / Microsoft Graph email + calendar | Reviewer |

## Optional Inputs
- Turn runtime (Phase 03) for long-running summarization batches.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] A concrete email/calendar provider connector is available
