# Phase 04 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Phase 03 turn pipeline | `docs/plans/phase-03-turn-runtime/`, merged `LeanKernel.Logic` pipeline | Rebuild maintainer |
| Current agent wiring / model aliases | `src/Services/LeanKernel.Gateway/Programs.cs`, `OpenAISettings.cs`, `AgentSettings.cs` | Rebuild maintainer |
| Source routing reference | `~/source/repos/leankernel/src/LeanKernel.Agents/Routing/*` | Reviewer |
| Source quality/enhancement reference | `~/source/repos/leankernel/src/LeanKernel.Agents/Quality/*`, `Enhancement/*` | Reviewer |
| Source orchestration/resilience reference | `~/source/repos/leankernel/src/LeanKernel.Agents/Orchestration/*`, `Resilience/*`, `Health/LiteLlmHealthProbe.cs` | Reviewer |
| Provider health signal | Rebuild `LiteLlmHealthCheck.cs`, `GBrainHealthCheck.cs` | Rebuild maintainer |

## Optional Inputs
- Source shadow-routing PRD and model-routing docs under `~/source/repos/leankernel/docs/features/`.
- Truth lifecycle and contradiction outputs from `docs/plans/phase-22-knowledge-integrity-truth-lifecycle/`.
- Telemetry evidence labels from `docs/plans/phase-17-model-telemetry-chat-history/`.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Phase 03 pipeline extension points confirmed available
- [ ] Grounding/contradiction signals from memory/truth pipeline available for quality-gate integration
