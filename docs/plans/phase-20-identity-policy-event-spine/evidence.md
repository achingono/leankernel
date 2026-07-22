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
| Canonical identity model | `src/Common/LeanKernel.Core/IdentityContext.cs` | Immutable identity context record |
| Event envelope | `src/Common/LeanKernel.Core/EventEnvelope.cs` | Universal event envelope with partitioning/correlation metadata |
| Turn event contract | `src/Common/LeanKernel.Core/Events/TurnEvent.cs` | Append-only turn event record |
| Tool call event contract | `src/Common/LeanKernel.Core/Events/ToolCallEvent.cs` | Append-only tool call event record |
| Telemetry event contract | `src/Common/LeanKernel.Core/Events/TelemetryEvent.cs` | Append-only telemetry event record |
| Policy context interface | `src/Common/LeanKernel.Logic/Policy/IPolicyContext.cs` | Policy evaluation context |
| Policy interface | `src/Common/LeanKernel.Logic/Policy/IPolicy.cs` | Domain policy contract |
| Policy evaluator | `src/Common/LeanKernel.Logic/Policy/IPolicyEvaluator.cs`, `src/Common/LeanKernel.Logic/Policy/PolicyEvaluator.cs` | Policy aggregation engine |
| Default policies | `src/Common/LeanKernel.Logic/Policy/IdentityLinkingPolicy.cs`, `src/Common/LeanKernel.Logic/Policy/MemoryAccessPolicy.cs`, `src/Common/LeanKernel.Logic/Policy/AuthorizationGatePolicy.cs`, `src/Common/LeanKernel.Logic/Policy/BudgetCheckPolicy.cs` | Built-in domain policies |
| Event collector | `src/Common/LeanKernel.Logic/Events/IEventCollector.cs`, `src/Common/LeanKernel.Logic/Events/EventCollector.cs` | Request-scoped event accumulation |
| Event store contract | `src/Common/LeanKernel.Logic/Events/IEventStore.cs`, `src/Common/LeanKernel.Logic/Events/DbEventStore.cs` | Async event persistence (durable DB-backed default) |
| Event spine persistence entity | `src/Common/LeanKernel.Core/Entities/EventEntity.cs` | Append-only event store record |
| Event spine migration | `src/Common/LeanKernel.Data/Migrations/20260722183902_AddEvents.cs` | Creates `Events` table and indexes |
| First-adopter migration | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs` | Emits turn and telemetry events alongside current persistence |
| DI registrations | `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs` | `AddEventSpine()` and `AddPolicyCore()` |
| Gateway wiring | `src/Services/LeanKernel.Gateway/Program.cs` | Calls `AddEventSpine()` and `AddPolicyCore()` |
| Gateway guardrail tests | `test/LeanKernel.Tests.Unit/Gateway/GatewayGuardrailTests.cs` | Verifies Gateway stays thin |
| Policy core tests | `test/LeanKernel.Tests.Unit/Policy/IdentityContextTests.cs`, `test/LeanKernel.Tests.Unit/Policy/PolicyEvaluatorTests.cs`, `test/LeanKernel.Tests.Unit/Policy/IdentityLinkingPolicyTests.cs`, `test/LeanKernel.Tests.Unit/Policy/BudgetCheckPolicyTests.cs` | Policy evaluation contract tests |
| Event spine tests | `test/LeanKernel.Tests.Unit/Events/EventCollectorTests.cs`, `test/LeanKernel.Tests.Unit/Events/EventEnvelopeTests.cs` | Event emission and envelope behavior |
| Durable event store tests | `test/LeanKernel.Tests.Unit/Events/DbEventStoreTests.cs` | Validates append and batch durable persistence into event spine table |
| Playwright endpoint tests | `test/LeanKernel.Tests.Playwright/PolicyAndEventStoreEndpointTests.cs` | Validates policy isolation behavior and durable event-store endpoint path |
| Sonar quality verification | `scripts/quality/sonarqube-scan.sh` | Quality gate passing (`new_coverage` >= 80) and no open unresolved Blocker/Critical/Major issues |
| Feature documentation | `docs/features/identity-policy-event-spine.md` | Phase 20 feature documentation |
