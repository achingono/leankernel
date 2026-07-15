# Phase 03 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Turn pipeline | `TurnPipeline` + `ITurnStage` sequence in `LeanKernel.Logic` | C# source |
| Context gatekeeper | Deny-by-default admission + budget enforcement | C# source |
| History shaping/compaction | Deterministic window selection, embedding-based Tier 2 compaction, and overflow summarization | C# source |
| Scoped retrieval | Retrieval scope policy merged through the gatekeeper | C# source |
| Embedding client | `IEmbeddingClient` abstraction + LiteLLM implementation | C# source |
| Long-running task support | Turn progress broker + continuation path | C# source |
| Configuration + validation | Budget/history/retrieval settings with startup checks | C# + appsettings |
| Tests | Unit + integration coverage for the above | xUnit projects |
| Documentation | Runtime-flow and feature docs updated | Markdown |

## Optional Outputs
- Per-turn admission trace surface reserved for Phase 08 diagnostics integration.

## Output Quality Checklist
- [x] All mandatory outputs produced
- [x] All outputs reviewed before gate
- [x] Evidence log updated with output references
