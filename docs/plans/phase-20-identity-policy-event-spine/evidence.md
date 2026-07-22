# Phase 20 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Current implemented identity contract | `docs/features/identity-partitioning.md` | Canonical source for present runtime identity invariants |
| Identity partitioning baseline | `docs/plans/phase-10-cross-channel-memory/`, `docs/plans/phase-15-channel-identity-mapping/`, `docs/plans/phase-16-identity-claims-context/` | Current identity and channel boundaries |
| Authorization baseline | `docs/plans/phase-19-authorization-permits-filters/` | Existing permit/filter/repository model |
| Telemetry baseline | `docs/plans/phase-17-model-telemetry-chat-history/` | Existing turn telemetry spine |
| Gateway composition root | `src/Services/LeanKernel.Gateway/Program.cs` | Host boundary to keep thin |
| First-adopter candidate paths | `src/Common/LeanKernel.Logic/Providers/`, `src/Common/LeanKernel.Logic/Telemetry/` | Likely early migration surfaces |
| Current history persistence model | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`, `src/Common/LeanKernel.Core/Entities/{SessionEntity,TurnEntity,TurnTelemetryEntity}.cs` | Existing session/turn/telemetry behavior the event spine must coexist with |
| Current permit/filter enforcement model | `src/Common/LeanKernel.Logic/Repositories/EntityRepository.cs`, `src/Common/LeanKernel.Logic/Filters/ScopeDrivenFilter.cs`, `src/Services/LeanKernel.Gateway/Providers/RequestContextPermitOfT.cs` | Existing fail-closed path the policy core must compose with |
