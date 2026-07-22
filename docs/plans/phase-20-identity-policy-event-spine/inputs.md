# Phase 20 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Current implemented identity contract | `docs/features/identity-partitioning.md` | Repository |
| Identity partitioning plan and implementation state | `docs/plans/phase-10-cross-channel-memory/`, `docs/plans/phase-15-channel-identity-mapping/`, `docs/plans/phase-16-identity-claims-context/` | Repository |
| Authorization/permit implementation | `docs/plans/phase-19-authorization-permits-filters/` | Repository |
| Telemetry implementation and schema | `docs/plans/phase-17-model-telemetry-chat-history/` | Repository |
| Current Gateway composition root | `src/Services/LeanKernel.Gateway/Program.cs` | Repository |
| Current logic-layer policy and context providers | `src/Common/LeanKernel.Logic/Providers/`, `src/Common/LeanKernel.Logic/TurnRuntime/` | Repository |
| Current entity model and EF context | `src/Common/LeanKernel.Core/Entities/`, `src/Common/LeanKernel.Data/EntityContext.cs` | Repository |
| Current transcript and telemetry persistence behavior | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`, `src/Common/LeanKernel.Core/Entities/{SessionEntity,TurnEntity,TurnTelemetryEntity}.cs` | Repository |

## Optional Inputs

- Existing famorize-style policy/repository patterns for reference.
- Any architecture review notes that identify host leakage or policy duplication.

## Input Validation Checklist
- [ ] Identity feature docs plus permit, telemetry, and memory phase docs reflect current implementation state
- [ ] Existing consumer paths are identified for first adopter migration
- [ ] Shared-library placement is accepted over a micro-service split
- [ ] Existing transcript, telemetry, and anonymous-session invariants are captured before designing the event spine
