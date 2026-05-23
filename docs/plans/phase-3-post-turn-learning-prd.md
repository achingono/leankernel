# Phase 3 Post-Turn Learning Pipeline PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers implementing asynchronous post-turn self-improvement processing.
- **Document type:** Product requirements document
- **Phase goal:** Add a bounded, background learning pipeline that extracts durable knowledge from completed turns without blocking the user-facing response path.
- **Plan review:** Reviewed by `gpt-5.4-mini`. Review outcome: proceed with an asynchronous learning project, account for the current `TurnEvent` shape (`Content`/`Role` instead of separate user+assistant fields), prefer built-in bounded-channel drop-oldest behavior, reuse existing refusal-pattern configuration, and protect shared knowledge-page updates from concurrent writers.

## Problem statement

LeanKernel already emits a `TurnEvent` after each completed assistant turn, but nothing currently consumes those events for post-turn learning. Phase 3 needs a best-effort learning subsystem that can queue assistant turn events, process them off the request path, and extract useful operational knowledge such as learned facts, repeated capability gaps, and lightweight engagement signals. The new subsystem must stay bounded, tolerate failures, and preserve response latency by never applying backpressure to the turn pipeline.

## Scope

This task will:

1. Add `LearningConfig` to `LeanKernel.Abstractions.Configuration` and wire it into `LeanKernelConfig`.
2. Add `ILearningStep`, `LearningStepResult`, `ISelfImprovementPipeline`, and `CapabilityGap` contracts/models.
3. Create the new `src/LeanKernel.Learning/` project.
4. Implement a bounded `TurnEventQueue` that acts as the `ITurnEventSink` for asynchronous learning.
5. Implement a resilient `SelfImprovementPipeline` that runs enabled learning steps in order and logs per-step outcomes.
6. Implement `LearningBackgroundWorker` to drain the queue in the background with bounded concurrency and graceful shutdown.
7. Implement `FactExtractionStep`, `CapabilityGapDetectionStep`, and `EngagementTrackingStep`.
8. Register learning services in DI and wire them into `LeanKernel.Gateway`.
9. Add focused unit tests for queue behavior, pipeline ordering, and each learning step.
10. Update configuration and contributor-facing docs for the new learning pipeline.

## Out of scope

- Blocking the user-facing response while learning runs.
- Using LLMs for capability-gap detection or engagement tracking.
- Introducing a new persistence system outside the existing knowledge service.
- Reworking the main turn pipeline beyond the already-supported turn-event hook.
- Running `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar locally when the toolchain is unavailable.

## Primary files

- `src/LeanKernel.Abstractions/Configuration/LearningConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Interfaces/ILearningStep.cs`
- `src/LeanKernel.Abstractions/Interfaces/ISelfImprovementPipeline.cs`
- `src/LeanKernel.Abstractions/Models/CapabilityGap.cs`
- `src/LeanKernel.Learning/LeanKernel.Learning.csproj`
- `src/LeanKernel.Learning/SelfImprovementPipeline.cs`
- `src/LeanKernel.Learning/TurnEventQueue.cs`
- `src/LeanKernel.Learning/LearningBackgroundWorker.cs`
- `src/LeanKernel.Learning/FactExtractionStep.cs`
- `src/LeanKernel.Learning/CapabilityGapDetectionStep.cs`
- `src/LeanKernel.Learning/EngagementTrackingStep.cs`
- `src/LeanKernel.Learning/ServiceCollectionExtensions.cs`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Tests.Unit/Learning/*.cs`
- `src/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj`
- `README.md`
- `docs/features/turn-pipeline.md`

## Functional requirements

### FR-1 Configuration and contracts

- `LearningConfig` must expose the requested defaults for enablement, step toggles, queue sizing, concurrency, and extraction model settings.
- `LeanKernelConfig` must expose `Learning` alongside other existing subsystem configs.
- `ILearningStep` must expose `Name`, `Order`, and `ProcessAsync(TurnEvent, CancellationToken)`.
- `ISelfImprovementPipeline` must process a `TurnEvent` asynchronously and never leak non-cancellation failures.
- `CapabilityGap` must capture category, description, session/turn provenance, detection timestamp, and occurrence count.

### FR-2 Queueing and worker behavior

- `TurnEventQueue` must implement `ITurnEventSink.PublishAsync` and enqueue events without blocking the caller.
- The queue must be bounded and configured to drop the oldest buffered event when full rather than backpressuring the turn pipeline.
- Queue overflow must be logged as a warning.
- `LearningBackgroundWorker` must continuously drain queued events while the host is running.
- The worker must cap concurrent learning execution with `LearningConfig.MaxConcurrentLearningTasks`.
- On shutdown, the worker must stop accepting new events, drain remaining work within a reasonable timeout, and log if any items are abandoned.

### FR-3 Learning pipeline behavior

- `SelfImprovementPipeline` must run registered `ILearningStep` implementations in ascending `Order`.
- The pipeline must log success/failure and learned item counts for each step.
- Step failures must be swallowed after logging so other steps can still run.
- Caller-initiated cancellation must still propagate.

### FR-4 Fact extraction

- `FactExtractionStep` must skip short turns below `MinTurnLengthForExtraction`.
- Fact extraction must call LiteLLM with the configured extraction model and low temperature.
- The prompt must ask for new factual information that should be remembered from the completed turn context.
- Because the current `TurnEvent` contains assistant content plus pre-turn context instead of separate user/assistant fields, extraction must use the available event content/history safely rather than assuming absent fields.
- Extracted facts must be written to the knowledge service under deterministic, sanitized page keys scoped by session and turn.
- Parsing must be defensive and deterministic for empty, plain-text, or list-style responses.

### FR-5 Capability gap detection

- `CapabilityGapDetectionStep` must use simple pattern matching rather than LLM calls.
- Detection should reuse existing refusal/inability patterns where practical instead of introducing a conflicting pattern source.
- Detected gaps must be persisted in knowledge storage with aggregation of repeated occurrences.
- Concurrent updates to shared capability-gap storage must be serialized to avoid lost updates.

### FR-6 Engagement tracking

- `EngagementTrackingStep` must update lightweight aggregate metrics such as total turns processed, topic frequencies, and simple satisfaction/engagement signals.
- Metrics must be persisted in the knowledge service under a stable page key.
- Concurrent updates to the shared engagement metrics page must be serialized to avoid lost updates.
- Topic derivation should stay lightweight and deterministic, using available retrieval/context signals before falling back to simple content heuristics.

### FR-7 DI and host integration

- `AddLeanKernelLearning(LearningConfig config)` must no-op when learning is disabled.
- When enabled, DI must register the queue, turn-event sink alias, self-improvement pipeline, hosted worker, and only the enabled learning steps.
- `LeanKernel.Gateway` must reference `LeanKernel.Learning` and call `AddLeanKernelLearning(leanKernelConfig.Learning)` during startup.
- The new project must be added to the solution and unit test project references.

### FR-8 Tests and docs

- Add unit tests for step ordering and failure swallowing in `SelfImprovementPipeline`.
- Add unit tests for bounded queue drop-oldest behavior.
- Add unit tests for fact extraction using a fake HTTP handler and fake knowledge service.
- Add unit tests for capability-gap detection pattern matching and aggregation.
- Add unit tests for engagement metric updates.
- Update appsettings and documentation to describe the new learning configuration and asynchronous post-turn stage.

## Design constraints

- Use file-scoped namespaces and nullable reference types.
- Keep learning orchestration in `LeanKernel.Learning`; contracts/config/models belong in `LeanKernel.Abstractions`.
- Learning must be asynchronous and best-effort.
- Non-cancellation failures must be logged, not thrown back into the request path.
- Use bounded queues and bounded concurrency.
- Keep heuristics deterministic and inexpensive.

## Validation plan

1. Review touched files for contract consistency, DI wiring, and compatibility with the existing `TurnEvent`/`ITurnEventSink` shapes in this repository.
2. Add focused unit tests covering queue semantics, learning-step execution, and persistence behavior.
3. Do not run `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar locally because the user explicitly stated the `dotnet` toolchain is unavailable in this environment.
4. Report the validation limitation clearly in the final summary.

## Acceptance criteria

- `LeanKernelConfig` exposes the requested `Learning` block.
- Learning contracts and `CapabilityGap` exist in `LeanKernel.Abstractions`.
- `LeanKernel.Learning` exists and is wired into the solution and gateway.
- `TurnEventQueue` asynchronously accepts events without backpressuring turn processing and drops the oldest buffered event when full.
- `LearningBackgroundWorker` drains queued events with bounded concurrency and graceful shutdown behavior.
- `FactExtractionStep`, `CapabilityGapDetectionStep`, and `EngagementTrackingStep` are implemented and registered behind configuration flags.
- Shared learning pages are updated safely under concurrent processing.
- Unit tests cover queue behavior, pipeline ordering/error handling, and each concrete learning step.
- Gateway configuration and docs describe the new asynchronous learning subsystem.
- The final report notes that `dotnet` and Sonar validation were skipped because the environment lacks the required tooling.
