# Memory Pipeline

The current memory pipeline lives in `LeanKernel.Logic` and persists scoped memory pages through `IMemoryClient`.

## Main Components

- `FactExtractionService`
- `MemoryPageParser`
- `MemoryPageRenderer`
- `MemoryPageNormalizer`
- `MemoryDimensionClassifier`
- `MemoryPageLinker`
- `MemoryGraphReasoner`
- `MemoryFieldRepairService`

Reference folder: [`../../src/Common/LeanKernel.Logic/Memory/`](../../src/Common/LeanKernel.Logic/Memory/)

## Runtime Behavior

At store time:

1. new facts are extracted from the latest interaction
2. seed pages are rendered
3. related memories are retrieved
4. pages are normalized into a structured form
5. normalized pages are saved back with scope-relative keys

At read time:

1. a scoped memory search runs
2. the top results are compacted into short summaries
3. those summaries are injected into the prompt context

Reference: [`../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs)

## Transport Boundary

The logic layer is provider-agnostic.

- `LeanKernel.Logic` depends on `IMemoryClient`
- `LeanKernel.Gateway` provides the GBrain-backed implementation
- memory pages are passed to the transport with scope-relative keys
- agent-session isolation is a separate runtime concern and is not controlled by `IdentityIsolationKeyProvider` on the memory path

Reference: [`../../src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs`](../../src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs)
