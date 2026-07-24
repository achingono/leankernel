# Phase 04 Model Intelligence And Response Quality

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add intelligent model selection and response-quality behavior to the rebuild's turn runtime so the `leankernel` agent can route turns to the most appropriate model, optionally shadow-compare candidates, gate low-quality outputs, enhance responses with citations/synthesis, degrade gracefully when providers are unhealthy, and orchestrate worker agents as tools. This ports the source repo's `Routing`, `Quality`, `Enhancement`, `Resilience`, and `Orchestration` behavior onto the Phase 03 pipeline while staying provider-agnostic through LiteLLM/`IChatClient`.

## Scope
This phase builds on the explicit turn pipeline from Phase 03. It covers model strategy selection, shadow routing, output quality gating, response post-processing, degradation policy, and multi-agent orchestration. It does not add new tools, channels, learning, scheduling, diagnostics UI, or Blazor UI.

## In Scope
- Policy-based model selection: a task-complexity scorer and a routing policy that maps complexity/task signals to a configured model alias, with escalation on failure/low quality.
- Shadow routing: run a candidate model in the background, compare against the primary, and record the comparison without affecting the returned response.
- Quality gates: pluggable checks (empty response, minimum length, constraint/coverage) that can trigger escalation or a controlled refusal-repair path.
- Response enhancement pipeline: citation injection, knowledge synthesis, and refusal interception as ordered post-processing steps.
- Graceful degradation policy driven by provider health so unhealthy models/tools fall back deterministically.
- Multi-agent orchestration: an orchestration decider, worker agents, and a worker-as-tool adapter so specialist agents can be invoked as callable tools.
- Configuration for routing policy, model aliases, shadow sampling rate, quality thresholds, and orchestration toggles under existing `Agents`/`OpenAI` sections.
- Tests for routing decisions, shadow isolation, quality-gate triggering, enhancement ordering, and orchestration dispatch.

## Out of Scope
- The turn pipeline/context gating itself (Phase 03 dependency).
- Spend guardrails and provider health probes' persistence/metrics (Phase 08), though this phase consumes a health signal.
- New tool categories (Phase 05).
- UI surfaces for routing/quality (Phase 09).

## Entry Criteria
- Phase 03 turn pipeline is merged and stages are extensible.
- LiteLLM-backed `IChatClient` routing is available and multiple model aliases are configurable.
- Source references captured as behavioral targets: `~/source/repos/leankernel/src/LeanKernel.Agents/Routing/PolicyModelSelector.cs`, `RoutedAgentStrategy.cs`, `TaskComplexityScorer.cs`, `ShadowRoutingStrategy.cs`, `ShadowComparer.cs`; `Quality/ResponseQualityGate.cs`, `MinLengthCheck.cs`, `EmptyResponseCheck.cs`, `ConstraintCoverageCheck.cs`; `Enhancement/ResponseEnhancementPipeline.cs`, `CitationInjectionStep.cs`, `KnowledgeSynthesisStep.cs`, `RefusalInterceptionStep.cs`; `Resilience/GracefulDegradationPolicy.cs`, `Health/LiteLlmHealthProbe.cs`; `Orchestration/OrchestratedAgentStrategy.cs`, `OrchestrationDecider.cs`, `WorkerAgent.cs`, `WorkerAsToolAdapter.cs`.

## Status
**Partial (8/11 gates complete)** — routing, shadow, and enhancement pipelines are implemented; remaining work is the intelligence brain delta focused on grounded-memory quality gates, contradiction-aware policies, and retrieval attribution.

## Design Delta: Intelligent Brain Track
- Add grounded-memory quality gates that require citation/source evidence for high-confidence responses.
- Add contradiction-aware response policy: detect conflicting memory candidates and choose deterministic fallback behavior (clarify, hedge, or abstain).
- Add retrieval attribution to enhancement steps so responses can label whether evidence came from raw document, synthesized facts, or pattern pages.
- Add ranking signals from post-turn feedback and telemetry labels to improve memory candidate ordering over time.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
