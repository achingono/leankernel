# PRD: Long-Running Task Continuation and Continuous Typing Indicator

> **Status:** Reviewed and approved for implementation.
> **Review record:** Drafted with Claude Fable 5; cross-model review performed with GPT-5.5 (verdict: rework). All blocker/major findings from the review are incorporated below (per-session coordination, turn metadata persistence, execution metadata, keepalive lifecycle, DI decorator registration, progress heartbeat, bounded directive parsing).

## Overview

Enable the agent to work on long-running tasks across multiple model turns with automatic continuation, user-visible progress updates, and a typing indicator that remains active for the full duration of the work — not just the first model invocation.

## Problem Statement

Today, `ChannelRouter.RouteInboundAsync` (`src/LeanKernel.Channels/ChannelRouter.cs:107-117`) starts a typing indicator once, awaits the full turn, sends the response, and stops typing. This breaks down for long-running tasks:

1. **Typing indicator expires.** Chat platforms auto-expire typing indicators server-side (Signal clients typically clear the indicator after roughly 15 seconds without a refresh; exact expiry is platform behavior, not controlled by our code). `SignalChannel.StartTypingAsync` is called exactly once per turn, so for any long turn the user sees the indicator disappear while the agent is still working.
2. **The agent turn ends before the task is complete.** The model frequently ends its turn with intent-to-continue language ("I'll now proceed to...") or exhausts the `FunctionInvokingChatClient` tool-iteration budget. Nothing detects this or prompts the model to continue, so work silently stalls.
3. **No progress updates.** The user receives zero feedback between sending a message and the final response. Combined with (1) and (2), users conclude the agent failed and re-prompt it manually ("continue", "are you still there?"), which pollutes history and wastes tokens.

## Goals

- Keep the typing indicator active continuously while a turn (including continuations) is in flight.
- Detect whether the task the user requested is complete at the end of each model turn.
- Automatically prompt the agent to continue incomplete tasks, bounded by hard limits.
- Send the user throttled, human-readable progress updates while long work is in flight.
- Serialize turns per session and let a new user message preempt an in-flight continuation loop.
- Preserve existing behavior for short conversational turns (no spam, no extra latency).

## Non-Goals (v1)

- Token-level streaming of model output to channels or the web UI.
- Durable resumption of tasks across process restarts (in-flight work is lost on restart).
- Force-cancelling a model/tool call already in flight when a new message arrives (preemption takes effect between continuation rounds; mid-round cancellation is future work).
- Progress UI redesign in the Blazor chat page (the existing spinner persists while the runtime call is in flight; richer progress rendering is a follow-up).
- Cross-instance coordination (LeanKernel is a modular monolith; in-process pub/sub is sufficient).

## User Stories

- As a Signal user, I see the typing indicator the entire time the agent is working, so I know it hasn't died.
- As a user who asked for a multi-step task, I get short progress notes ("Searched the wiki, now drafting the summary...") when work takes longer than a threshold.
- As a user, when the model pauses mid-task, the system automatically tells it to continue — I never have to type "continue" myself.
- As an operator, I can bound auto-continuation (max rounds, max wall-clock, spend guard) and disable the feature entirely via config.
- As a user, if I send a new message while the agent is auto-continuing, my new message takes priority and the continuation loop stops.

## Current Architecture (Relevant Surfaces)

| Surface | File | Role |
| ------- | ---- | ---- |
| Channel routing | `src/LeanKernel.Channels/ChannelRouter.cs` | Starts/stops typing once, awaits `IAgentRuntime.RunTurnDetailedAsync`, sends response. Registered as **singleton** (`ServiceCollectionExtensions.cs:25`) |
| Typing transport | `src/LeanKernel.Channels/SignalChannel.cs` | `StartTypingAsync`/`StopTypingAsync` → signal-cli REST, 5s request timeout |
| Inbound handling | `src/LeanKernel.Channels/ChannelHostedService.cs` | Event handler per channel; **no per-session serialization today** — concurrent messages can produce concurrent turns |
| Turn pipeline | `src/LeanKernel.Agents/TurnPipeline.cs` | Persist → gate → assemble → invoke strategy → enhance → persist; wraps tools per-turn via private `TurnToolInvocationTracker`; registered **scoped** as `ITurnPipeline` (`AgentsServiceCollectionExtensions.cs`) |
| Strategy invocation | `src/LeanKernel.Agents/Strategies/*`, `Orchestration/*` | Single blocking `GetResponseAsync` via `FunctionInvokingChatClient`; returns `string` only — no finish-reason or iteration metadata surfaces today |
| Directive precedent | `TurnPipeline.SignalAttachmentDirectiveRegex` | Fenced ```` ```signal-attachments ```` blocks parsed and stripped from responses |
| Turn model | `src/LeanKernel.Abstractions/Models/ConversationTurn.cs` | Role/Content/Timestamp/TurnId/compaction fields — **no metadata field today** |
| Response model | `src/LeanKernel.Abstractions/Models/AgentResponse.cs` | Content + attachments — **no execution metadata today** |
| Contracts | `src/LeanKernel.Abstractions/Interfaces/*` | `IChannel`, `ITurnPipeline`, `IAgentRuntime`, `ITurnEventSink` |

## Design

Five cooperating components, each feature-local to its owning project.

### 1. Per-session turn coordination (`LeanKernel.Agents`)

**New:** `SessionTurnCoordinator` (singleton) providing:

- **Serialization:** a per-session `SemaphoreSlim(1,1)` (keyed dictionary with cleanup) so at most one turn runs per session. `ChannelRouter` (and any other runtime caller) acquires the session lock for the duration of the turn, giving deterministic ordering for rapid successive messages.
- **Preemption:** `BeginTurn(sessionId)` returns a `TurnLease` exposing a `PreemptionRequested` token/flag. `NotifyInbound(sessionId)` (called by `ChannelRouter` when a new message for the session arrives and queues on the lock) sets the flag on the active lease. The continuation loop checks the flag **between rounds** and stops immediately; the queued message then acquires the lock and processes normally. A running round is not force-cancelled in v1.

This makes the preemption story concrete and closes the pre-existing concurrent-turn race for channel traffic.

### 2. Typing keepalive (`LeanKernel.Channels`)

**New:** `TypingIndicatorKeepAlive` — an `IAsyncDisposable` scope that refreshes `channel.StartTypingAsync(recipientId)` on a `PeriodicTimer` every `KeepAliveSeconds` (default **8s**, comfortably under typical platform expiry) until stopped.

Lifecycle requirements (review finding):

- `StopAsync()` is **idempotent**: first call cancels and awaits the timer loop, then sends `StopTypingAsync` exactly once; subsequent calls are no-ops. `DisposeAsync()` simply calls `StopAsync()`.
- The stop call uses its own bounded token (not the possibly-cancelled turn token) so the indicator is cleared even when the turn is cancelled.
- Refresh failures are logged at Debug and never fail the turn (same policy as today's `TrySignalTypingAsync`).
- Uses `TimeProvider` for testability.

`ChannelRouter.RouteInboundAsync` integration:

```csharp
await using var typing = TypingIndicatorKeepAlive.Start(channel, message.SenderId, _config.Typing, _timeProvider, _logger);
try
{
    var response = await _runtime.RunTurnDetailedAsync(runtimeMessage, ct).ConfigureAwait(false);
    await typing.StopAsync().ConfigureAwait(false);   // idempotent; also invoked by DisposeAsync on failure paths
    await channel.SendAsync(message.SenderId, response.Content, response.Attachments, ct).ConfigureAwait(false);
}
finally { await typing.StopAsync().ConfigureAwait(false); }
```

The keepalive naturally covers auto-continuation rounds because it wraps the entire runtime call.

### 3. Turn progress broker (`LeanKernel.Abstractions` contract, `LeanKernel.Agents` publisher, `LeanKernel.Channels` consumer)

A lightweight in-process pub/sub so deep pipeline layers can surface progress without new coupling:

- **Contract** (`LeanKernel.Abstractions.Interfaces`):

```csharp
public interface ITurnProgressBroker
{
    IDisposable Subscribe(string sessionId, Func<TurnProgressUpdate, Task> handler);
    Task PublishAsync(TurnProgressUpdate update, CancellationToken ct = default);
}

public sealed record TurnProgressUpdate(
    string SessionId, string TurnId, TurnProgressKind Kind,
    string? ToolName, string? Message, DateTimeOffset Timestamp);

public enum TurnProgressKind { ToolStarted, ToolCompleted, ContinuationStarted, StatusNote, Heartbeat }
```

- **Publisher:** `TurnPipeline.WrapToolsForTurn` (the existing tool-wrap point) publishes `ToolStarted`/`ToolCompleted`. The continuation decorator publishes `ContinuationStarted` and model-authored `StatusNote`s.
- **Consumer:** `ChannelRouter` subscribes by session id for the duration of the turn and converts updates into user-facing messages subject to throttling:
  - No progress message before `InitialSilenceSeconds` (default **20s**) of turn wall-clock.
  - At most one progress message per `MinIntervalSeconds` (default **45s**).
  - Model-authored `StatusNote`s take priority over synthesized tool summaries.
  - **Heartbeat (review finding):** if no broker event has arrived for `HeartbeatSeconds` (default **90s**) after the initial-silence window (e.g., one long model-only generation), the router sends a generic "⏳ Still working…" note, subject to the same min-interval throttle.
- **Session id resolution:** `ChannelRouter` resolves the session id exactly the way the pipeline does — via `ISessionStore.GetOrCreateSessionIdAsync(channelId, senderId)` — before subscribing, so subscription keys always match publisher keys.
- Broker registration is a DI singleton; publish is exception-isolated (a failing subscriber never breaks the pipeline).
- Progress messages are **ephemeral channel sends** — never persisted as assistant turns.

### 4. Task completion detection (`LeanKernel.Agents`)

**New:** `TaskCompletionEvaluator` decides, per completed model pass, whether the user's task is finished. Layered signals, cheapest first:

1. **Structured directive (primary).** The system prompt (instruction text added to the prompt-assembly resources) instructs the model to end every response to a multi-step task with a fenced block, following the existing `signal-attachments` precedent:

   ````
   ```task-status
   { "status": "in_progress", "note": "Finished research; drafting the summary next." }
   ```
   ````

   The pipeline parses and strips this block using a compiled regex **with an explicit `Regex` match timeout and a bounded JSON payload size** (review finding), mirroring `SignalAttachmentDirectiveRegex`. `status ∈ { complete, in_progress, blocked }`; `note` is the user-facing progress message.
2. **Heuristic fallback.** When no directive is present: match trailing intent-to-continue phrases (configurable list, e.g. "I'll now", "next, I will", "proceeding to", "let me continue") against the tail of the response.
3. **Classifier fallback (config-gated, off by default).** A single cheap-model call ("Does this response complete the user's request? YES/NO") routed to the low tier via the existing `PolicyModelSelector`. Only invoked when heuristics are ambiguous and `Continuation:UseClassifier` is true.

Evaluation output: `TaskCompletionAssessment { IsComplete, Confidence, ProgressNote, Reason }`.

**Fail-safe default:** when signals are absent or ambiguous, treat the task as **complete** (never loop on uncertainty).

**Intent gating:** continuation only applies when the turn plausibly involved a task: **turns with zero tool invocations never auto-continue**, and the original user message must pass a cheap gate (length/imperative heuristics). This prevents Q&A chit-chat from looping.

### 5. Auto-continuation loop (`LeanKernel.Agents`)

**New:** `ContinuationTurnPipeline` — a decorator implementing `ITurnPipeline` around the concrete `TurnPipeline`.

**DI registration (review finding — explicit):** in `AgentsServiceCollectionExtensions`, register the concrete pipeline and the decorator with the same scoped lifetime:

```csharp
services.AddScoped<TurnPipeline>();
services.AddScoped<ITurnPipeline>(sp => new ContinuationTurnPipeline(
    sp.GetRequiredService<TurnPipeline>(),
    sp.GetRequiredService<TaskCompletionEvaluator>(),
    sp.GetRequiredService<SessionTurnCoordinator>(),
    sp.GetService<ITurnProgressBroker>(),
    sp.GetService<ISpendGuardService>(),
    sp.GetRequiredService<IOptions<LeanKernelConfig>>(),
    sp.GetRequiredService<ILogger<ContinuationTurnPipeline>>()));
```

Placing continuation at the pipeline level (not in `ChannelRouter`) means every caller — Signal, future channels, the Blazor chat — gets continuation for free, and the web spinner naturally stays active.

Loop:

```
result = inner.ProcessDetailedAsync(message)
while (config.Enabled
       && result.Execution?.ToolInvocationCount > 0        // zero-tool turns never continue
       && rounds < MaxAutoContinuations
       && elapsed < MaxTotalDuration
       && evaluator.Assess(result).IsComplete == false
       && spend guard allows
       && lease.PreemptionRequested == false
       && progress is being made):
    publish ContinuationStarted + StatusNote(assessment.ProgressNote)   // → user sees an update
    result = inner.ProcessDetailedAsync(syntheticContinue)              // typing keepalive still active
return final result
```

Specifics:

- **Execution metadata (review finding).** Strategy results currently surface only a string; the decorator needs facts to decide. Extend `AgentResponse` with an optional `TurnExecutionInfo` record populated by `TurnPipeline`:

  ```csharp
  public sealed record TurnExecutionInfo(
      int ToolInvocationCount, int SuccessfulToolInvocations,
      TaskStatusDirective? TaskStatus,   // parsed + stripped task-status block, if any
      string? ModelUsed);
  ```

  This is additive (nullable property), so existing consumers are unaffected. The private `TurnToolInvocationTracker` counts feed it directly.
- **Synthetic continuation message:** `LeanKernelMessage` with content `"Continue working on the task. Do not repeat completed steps; pick up where you left off."`, same `SessionId`/`SenderId`/`ChannelId`, metadata `auto_continuation=true, continuation_round=N`.
- **Turn metadata persistence (review finding — blocker fix).** `ConversationTurn` gains an optional `Metadata` dictionary (`IReadOnlyDictionary<string, string>?`), persisted by `LeanKernel.Persistence` (new nullable JSON column + migration). `TurnPipeline.AppendUserTurnAsync` copies `LeanKernelMessage.Metadata` through. History assembly keeps continuation turns (the model needs them for context), but UI history rendering and exports can filter or badge turns tagged `auto_continuation`. This keeps the audit trail honest without polluting what the user sees.
- **Limits** (config): `MaxAutoContinuations` default **3**; `MaxTotalDurationSeconds` default **600**; both checked before each round.
- **No-progress guard:** if two consecutive rounds produce near-identical responses (normalized-text similarity above threshold) or a round performs zero tool calls while claiming `in_progress`, stop and append a note that work halted.
- **Spend guard integration:** consult `ISpendGuardService.Evaluate` before each round; a `Block` decision terminates the loop with the block reason appended.
- **Preemption:** checks the `TurnLease.PreemptionRequested` flag from `SessionTurnCoordinator` between rounds (see §1); a queued user message always wins.
- **Response composition:** the returned `AgentResponse` is the *final* round's response (each round is persisted normally per-round). When the loop terminates without completion (limit hit, blocked, preempted), the final response appends a brief honest note (e.g. "I've paused here — say 'continue' to resume.").
- **Cancellation:** honors the incoming `CancellationToken` throughout; a cancelled round does not trigger another.

### Configuration

New `ContinuationConfig` bound at `LeanKernel:Continuation`, plus typing settings on `ChannelsConfig` (`LeanKernel:Channels:Typing`):

```jsonc
"LeanKernel": {
  "Channels": {
    "Typing": {
      "Enabled": true,
      "KeepAliveSeconds": 8
    }
  },
  "Continuation": {
    "Enabled": true,
    "MaxAutoContinuations": 3,
    "MaxTotalDurationSeconds": 600,
    "UseClassifier": false,
    "ContinuePhrases": [],            // optional additions to built-in heuristic list
    "Progress": {
      "Enabled": true,
      "InitialSilenceSeconds": 20,
      "MinIntervalSeconds": 45,
      "HeartbeatSeconds": 90
    }
  }
}
```

Defaults preserve current behavior when `Enabled: false` on either feature.

## Functional Requirements

### FR-1 Continuous typing indicator

- Typing indicator refreshes on a fixed cadence while any part of the turn (including continuation rounds) is in flight.
- Refresh cadence configurable; default below typical platform expiry windows.
- Refresh failures are non-fatal and logged at Debug.
- `StopTypingAsync` is sent exactly once when the turn finishes (success, failure, or cancellation), using a bounded token independent of the turn token; the stop path is idempotent.

### FR-2 Progress updates

- Tool start/completion and continuation milestones are published to an in-process broker keyed by session.
- Channel router converts updates to user messages, throttled by initial-silence and min-interval settings, with a heartbeat fallback for long event-free periods.
- Progress messages are clearly interim and never persisted as assistant turns.
- Zero progress messages are sent for turns that finish inside the initial-silence window.

### FR-3 Task completion detection

- Model is prompted to emit a `task-status` directive; pipeline strips it from the user-visible response with bounded regex/JSON parsing.
- Directive absent → heuristics; heuristics ambiguous and classifier enabled → cheap-model classification; still ambiguous → treat as complete.
- Turns with zero tool invocations are always treated as complete.

### FR-4 Auto-continuation

- Incomplete assessments trigger a synthetic continuation turn, up to configured round/duration limits.
- Each continuation round emits a progress update to the user before the round begins.
- Spend guard, no-progress detection, preemption, and cancellation each terminate the loop cleanly with a reason code.
- Loop termination without completion appends an honest user-facing note.

### FR-5 Serialization, preemption, and user intent

- At most one turn runs per session; rapid successive messages process in order.
- A new inbound message for the same session sets preemption and stops the continuation loop before the next round.
- A user message consisting of a continuation request ("continue", "keep going") while no loop is active processes as a normal turn (no special casing in v1).

### FR-6 Persistence and history integrity

- `ConversationTurn` carries optional metadata; synthetic continuation turns are persisted and tagged `auto_continuation=true`.
- History assembly includes continuation turns for model context; UI rendering can filter/badge them.
- Persistence schema change ships with a migration and is backward-compatible (nullable column).

### FR-7 Observability

- Metrics: continuation rounds per turn, loop terminations by reason, progress messages sent, typing refreshes sent (extend `LeanKernelMetrics`).
- Structured logs at Information for round start/stop with reason codes.
- Turn events (`ITurnEventSink`) fire per round exactly as today (each round is a real pipeline pass).

## Scope of Change (Files)

| Project | Change |
| ------- | ------ |
| `LeanKernel.Abstractions` | New: `ITurnProgressBroker`, `TurnProgressUpdate`, `TurnProgressKind`, `TaskCompletionAssessment`, `TurnExecutionInfo`, `TaskStatusDirective`; `ContinuationConfig`; `TypingConfig` on `ChannelsConfig`; extend `LeanKernelConfig`; add nullable `Metadata` to `ConversationTurn`; add nullable `Execution` to `AgentResponse` |
| `LeanKernel.Agents` | New: `TurnProgressBroker`, `SessionTurnCoordinator` (+ `TurnLease`), `TaskCompletionEvaluator`, `ContinuationTurnPipeline`; modify `TurnPipeline` (publish tool progress from `WrapToolsForTurn`, parse/strip `task-status`, populate `TurnExecutionInfo`, persist message metadata); modify `AgentsServiceCollectionExtensions` (concrete + decorator registration) |
| `LeanKernel.Channels` | New: `TypingIndicatorKeepAlive`; modify `ChannelRouter` (keepalive scope, session-lock acquisition + `NotifyInbound`, progress subscription + throttled sends + heartbeat) |
| `LeanKernel.Context` | Prompt-assembly instruction text for the `task-status` directive (resource/config-local) |
| `LeanKernel.Persistence` | `Metadata` column on turns (nullable JSON) + migration; store/read mapping |
| `LeanKernel.Gateway` / host composition | Bind new config sections; `appsettings.json` defaults; optional history badge/filter for `auto_continuation` turns |
| `LeanKernel.Diagnostics` | New metrics counters |
| Tests | Unit: keepalive cadence + idempotent stop (fake `TimeProvider`/channel), directive parsing (incl. malformed/oversized payloads), completion heuristics matrix, continuation loop guards (limits, no-progress, preemption, spend-block, zero-tool), coordinator serialization/preemption, router throttling + heartbeat. Integration: end-to-end continuation with a scripted strategy stub; persistence round-trip of turn metadata |
| Docs | `README.md` config table; `docs/features/` new page "Long-running tasks & progress updates"; `docs/development` notes |

## Implementation Phases

1. **Phase 1 — Typing keepalive** (small, independent, immediate UX win): `TypingIndicatorKeepAlive` + `ChannelRouter` integration + `TypingConfig` + tests.
2. **Phase 2 — Session coordination + progress broker**: `SessionTurnCoordinator`, contracts, broker, `TurnPipeline` tool-progress publishing, router subscription/throttling/heartbeat + tests.
3. **Phase 3 — Completion detection + auto-continuation**: `ConversationTurn`/`AgentResponse` extensions + persistence migration, directive protocol + prompt changes, evaluator, `ContinuationTurnPipeline` + DI decorator, guards + tests.
4. **Phase 4 — Hardening & docs**: metrics, config documentation, feature docs, full test/coverage/Sonar pass.

Each phase is independently shippable and gated behind config defaults that preserve current behavior if disabled.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Infinite/expensive continuation loops | Hard round + duration caps, spend guard consult per round, no-progress similarity guard, zero-tool short circuit, fail-safe "complete" default |
| Progress message spam | Initial-silence window + min-interval throttle; heartbeat shares the throttle |
| Model ignores `task-status` directive | Heuristic + optional classifier fallback; fail-safe default is "complete" (no loop) |
| Duplicate typing calls hammering signal-cli | 8s cadence ≈ 7 requests/min max per active turn; failures logged and ignored |
| Double/missed `StopTypingAsync` | Idempotent `StopAsync`; timer cancelled and awaited before the single stop send; bounded independent token |
| Continuation fights a new user message | Per-session lock serializes turns; preemption flag checked between rounds; new message always wins |
| History pollution by synthetic turns | Turn metadata persisted; UI filters/badges `auto_continuation` turns; model context keeps them |
| Captive-dependency/DI mistakes | Explicit concrete + factory decorator registration with matching scoped lifetimes; scope-validated in tests |
| Regex/JSON parsing DoS on directive | `Regex` match timeout + bounded payload size |
| Per-session lock leak/starvation | Coordinator evicts idle session entries; lock acquisition honors cancellation |

## Success Metrics

- Typing indicator visible for the (near-)entire duration of turns >30s on Signal (manual verification + refresh counter metric).
- Reduction in user-sent "continue"/"are you there" messages in long-task sessions.
- Zero continuation loops exceeding configured caps in logs.
- All quality gates pass: `dotnet test`, `scripts/quality/test-coverage.sh`, `scripts/quality/sonarqube-scan.sh`.

## Open Questions

1. Should progress `StatusNote`s be persisted as a distinct turn type for the web UI to render later, rather than ephemeral? (v1: ephemeral; revisit with web parity.)
2. Should `MaxAutoContinuations` be adaptive to task complexity via `TaskComplexityScorer`? (v1: fixed; noted as future work.)
3. Should preemption force-cancel the in-flight round's `CancellationToken` rather than waiting for the round boundary? (v1: round boundary; mid-round cancellation needs careful tool-side cleanup.)
