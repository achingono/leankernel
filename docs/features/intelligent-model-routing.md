# Intelligent Model Routing

This document reflects the current implementation in `src/LeanKernel.Agents`.

## Components

| Component | Responsibility |
| --- | --- |
| `TaskComplexityScorer` | Produces a deterministic `0.0`-`1.0` complexity score from message size, history, tool availability, multi-step instructions, and long-context signals. |
| `PolicyModelSelector` | Maps the score to `Economy`, `Standard`, or `Premium` and resolves the configured model name for that tier. |
| `EscalationPolicy` | Promotes failed routed attempts from `Economy → Standard → Premium` while enforcing `MaxEscalationAttempts`. |
| `RoutedAgentStrategy` | Scores, selects, invokes LiteLLM through `AgentFactory`, evaluates quality, and escalates when allowed. |
| `ShadowRoutingStrategy` | Decorates the authoritative strategy, runs a configured shadow model in parallel, persists comparison diagnostics, and still returns only the primary response. |
| `ShadowComparer` | Produces deterministic comparison metadata such as length ratio, refusal detection, and notable-difference notes. |
| `StaticAgentStrategy` | Preserves the pre-routing single-model path when routing is disabled. |
| `AgentFactory` | Reuses LiteLLM-backed `IChatClient` instances per model name. |

## Configuration

Routing configuration lives under `LeanKernel:Routing`.

| Field | Default |
| --- | --- |
| `Enabled` | `false` |
| `QualityMinOutputLength` | `50` |
| `QualityMinConstraintCoverage` | `0.6` |
| `MaxEscalationAttempts` | `2` |
| `RefusalPatterns` | `I cannot`, `I'm sorry, I can't`, `As an AI language model`, `I'm not able to`, `I don't have the ability` |
| `ShadowRoutingEnabled` | `false` |
| `ShadowModel` | empty |
| `Economy.Model` | `gpt-4o-mini` |
| `Economy.MaxTokens` | `4096` |
| `Economy.CostWeight` | `0.3` |
| `Standard.Model` | `gpt-4o` |
| `Standard.MaxTokens` | `8192` |
| `Standard.CostWeight` | `1.0` |
| `Premium.Model` | `claude-sonnet-4-20250514` |
| `Premium.MaxTokens` | `16384` |
| `Premium.CostWeight` | `3.0` |
| `Scoring.HighComplexityTokenThreshold` | `2000` |
| `Scoring.MediumComplexityTokenThreshold` | `500` |
| `Scoring.ToolUsageComplexityBoost` | `0.3` |
| `Scoring.MultiTurnComplexityBoost` | `0.2` |
| `Scoring.LongContextComplexityBoost` | `0.2` |

## Routing flow

1. `TurnPipeline` builds an `AgentStrategyContext` with the session id, turn id, history, and visible tool names.
2. When `LeanKernel:Routing:Enabled=false`, DI resolves `StaticAgentStrategy` and the default LiteLLM model remains authoritative.
3. When routing is enabled, DI resolves `RoutedAgentStrategy`.
4. `TaskComplexityScorer` scores the request and records contributing factors.
5. `PolicyModelSelector` chooses the initial tier:
   - score `< 0.3` → `Economy`
   - score `0.3`-`0.7` → `Standard`
   - score `> 0.7` → `Premium`
6. `AgentFactory.GetChatClientForModel` creates or reuses a LiteLLM-backed `IChatClient` for the chosen model.
7. `RoutedAgentStrategy` evaluates the response for, in deterministic order:
   - empty output
   - minimum output length
   - refusal-like output using configured `RefusalPatterns`
   - prompt-to-response constraint coverage
8. If the quality gate fails and escalation is still allowed, `EscalationPolicy` promotes the request to the next tier and the next response is evaluated again.
9. When `ShadowRoutingEnabled=true` and `ShadowModel` is non-empty, DI wraps the authoritative strategy in `ShadowRoutingStrategy`, which launches the shadow call in parallel and records a `ShadowRoutingResult` diagnostic entry.
10. The final selected model is surfaced through `TurnEvent.ModelUsed`, and routed executions also attach a structured `RoutingDecision` payload.

## Diagnostics

Routing decisions are observable in four ways:

- `RoutedAgentStrategy` emits structured logs containing session id, turn id, selected model, tier, score, escalation attempt, factors, quality outcome, and per-check summaries.
- `TurnPipeline` publishes the final `RoutingDecision` in `TurnEvent` so downstream event consumers can inspect the final routed path.
- `TurnPipeline` also records the final `QualityGateResult` through `DiagnosticsCollector.RecordQualityGateAsync` whenever the active strategy returns structured quality metadata.
- `ShadowRoutingStrategy` persists a `DiagnosticEntry` with category `Shadow` containing the authoritative response, shadow response, latencies, token counts, and comparison metadata whenever a diagnostics sink is available.

`TurnPipeline` records a structured `RoutingDecision` through `DiagnosticsCollector.RecordModelRoutingAsync` whenever the active strategy returns routing metadata.

## Current limitations

- Shadow routing is diagnostic-only: the shadow response is persisted for comparison but never returned to the caller and any shadow failure is treated as best-effort telemetry.
- Quality checks are heuristic and intentionally deterministic; they do not inspect provider-native safety metadata.
- Routing is disabled by default so existing deployments continue to use the single-model path until explicitly enabled.
