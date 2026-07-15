# Phase 17 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Model/usage discarded on persist | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs:215-232` | `ToTurnEntity` keeps only role/content/author/timestamp |
| Store path with response messages | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs:76-97` | `StoreChatHistoryAsync` has `InvokedContext.ResponseMessages` |
| JSON metadata attachment point | `src/Common/LeanKernel.Core/Entities/TurnEntity.cs:50-53` | `Metadata` (nullable JSON) per turn |
| Proxy route telemetry | `config/litellm/leankernel_litellm_callbacks.py:89-108` | Records requested_model, provider, model_id, api_base, response_model |
| Provider normalization | `config/litellm/leankernel_litellm_callbacks.py:333-337` | `normalize_provider` / `PROVIDER_ALIASES` to reuse for consistency |
| Model configuration | `src/Common/LeanKernel.Logic/Configuration/OpenAISettings.cs:21,26` | DefaultModel / ToolModel aliases requested from LiteLLM |
| Consumers | `docs/plans/phase-04-model-intelligence/index.md`, `phase-07-learning-scheduler/index.md`, `phase-08-diagnostics-ops/index.md` | Routing/cost profiles, learning, spend guard |
