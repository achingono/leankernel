# Phase 17 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Chat history persistence | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs:76-97` (`StoreChatHistoryAsync`), `:215-232` (`ToTurnEntity`) | Rebuild maintainer |
| Turn entity + JSON metadata | `src/Common/LeanKernel.Core/Entities/TurnEntity.cs:50-53` (`Metadata`) | Rebuild maintainer |
| Session entity + metadata | `src/Common/LeanKernel.Core/Entities/SessionEntity.cs:47-48` (`Metadata`) | Rebuild maintainer |
| LiteLLM route telemetry | `config/litellm/leankernel_litellm_callbacks.py:89-108` (`record_route`: provider, model_id, api_base, response_model) | Rebuild maintainer |
| Chat client / model settings | `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs:101,111`, `OpenAISettings.cs` (DefaultModel/ToolModel) | Rebuild maintainer |
| Persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |
| Partitioning permit | `IPermit` (TenantId/UserId/ChannelId) used in `DbChatHistoryProvider` | Rebuild maintainer |

## Optional Inputs
- Phase 04 model intelligence (`docs/plans/phase-04-model-intelligence/`) — telemetry consumer for routing/cost profiles.
- Phase 07 learning (`docs/plans/phase-07-learning-scheduler/`) — telemetry consumer for supervised tuning.
- Phase 08 spend guard (`docs/plans/phase-08-diagnostics-ops/`) — telemetry consumer for budget enforcement.

## Input Validation Checklist
- [x] LiteLLM cost surface confirmed — `x-litellm-response-cost` header available on response; fallback to token-based estimate configured
- [x] MAF `ChatResponse` exposes `ModelId` and `UsageDetails` on the invocation path — confirmed via `TelemetryCapturingChatClient` decorator wrapping `IChatClient`
- [x] Correlation id strategy deferred — initial implementation captures model/provider/usage/cost from HTTP response without proxy reconciliation; full correlation deferred to follow-up
