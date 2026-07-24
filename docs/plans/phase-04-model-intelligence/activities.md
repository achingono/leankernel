# Phase 04 Activities

## Step-By-Step Activities
1. Define a model-selection abstraction: a `TaskComplexityScorer` producing a stable complexity signal and a `PolicyModelSelector` mapping signals + configured policy to a model alias.
2. Implement a routed agent strategy that plugs into the Phase 03 pipeline model-invocation stage, replacing the single fixed alias with policy-driven selection and failure escalation.
3. Implement shadow routing: sample a fraction of turns, invoke a candidate model out-of-band, compare via a `ShadowComparer`, and record the result without mutating the returned response.
4. Implement quality gates: an ordered set of checks (empty, min-length, constraint/coverage) with a gate result that can trigger escalation to a stronger model or a bounded refusal-repair retry.
5. Implement the response enhancement pipeline: ordered steps for citation injection, knowledge synthesis, and refusal interception, applied after the model call and before persistence.
6. Implement a graceful degradation policy that consumes provider health and deterministically falls back (alternate alias, reduced toolset, or safe message) when a provider is unhealthy.
7. Implement multi-agent orchestration: an orchestration decider, worker agents, and a worker-as-tool adapter exposing workers as callable tools to the primary agent.
8. Add configuration (routing policy table, model aliases, shadow rate, quality thresholds, orchestration toggle) under existing `Agents`/`OpenAI`; validate at startup.
9. Add tests: routing decision determinism, shadow side-effect isolation, quality-gate triggering + escalation, enhancement step ordering, degradation fallback, orchestration dispatch.
10. Document routing, shadow, quality, enhancement, and orchestration in `docs/features/`.

### Intelligent Brain Delta Activities
11. Add grounded-memory quality checks that require evidence attribution before high-confidence final responses.
12. Add contradiction-aware gate behavior for conflicting memory candidates (clarify, hedge, or abstain policy).
13. Thread telemetry feedback signals (grounded vs ungrounded outcomes) into ranking/escalation tuning inputs.
14. Add tests for grounded citation enforcement and contradiction-policy branches.

## Review Focus
- Routing decisions are deterministic given the same inputs and policy.
- Shadow routing never affects the primary response or persistence.
- Quality-gate escalation is bounded (no infinite retry loops).
- Enhancement steps are ordered and idempotent.
- Degradation is deterministic and observable.
- Orchestration respects identity partitioning and tool governance.
