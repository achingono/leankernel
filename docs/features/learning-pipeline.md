# Learning Pipeline

`LeanKernel.Services.Learning` runs asynchronous turn learning outside the gateway
request path.

## Service Boundary

- Project: `src/Services/LeanKernel.Services.Learning`
- References shared runtime logic from `LeanKernel.Core`, `LeanKernel.Logic`, `LeanKernel.Data`,
  and `LeanKernel.Services.Common`.
- Does not reference gateway host internals.

## Runtime Flow

1. A bounded in-memory `TurnEventQueue` accepts turn events.
2. `LearningBackgroundWorker` drains the queue.
3. Pipeline steps execute in order: fact extraction, identity intent, capability gap, engagement.
4. `KnowledgePageUpdateCoordinator` persists learned facts to memory keys.

## Components

- `src/Services/LeanKernel.Services.Learning/TurnEventQueue.cs`
- `src/Services/LeanKernel.Services.Learning/LearningBackgroundWorker.cs`
- `src/Services/LeanKernel.Services.Learning/Steps/FactExtractionStep.cs`
- `src/Services/LeanKernel.Services.Learning/KnowledgePageUpdateCoordinator.cs`
- `src/Services/LeanKernel.Services.Learning/Onboarding/OnboardingGapDetector.cs`
- `src/Services/LeanKernel.Services.Learning/Onboarding/OnboardingDirectiveBuilder.cs`

## Configuration

- `Learning:Enabled`
- `Learning:TurnQueueCapacity`
- `Learning:MaxConcurrency`
