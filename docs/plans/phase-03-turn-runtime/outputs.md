# Phase 03 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Turn pipeline | `TurnPipeline` + `ITurnStage` sequence in `LeanKernel.Logic` | C# source |
| Context gatekeeper | Deny-by-default admission + budget enforcement | C# source |
| History shaping/compaction | Deterministic window selection and overflow compaction | C# source |
| Scoped retrieval | Retrieval scope policy merged through the gatekeeper | C# source |
| Long-running task support | Turn progress broker + continuation path | C# source |
| Configuration + validation | Budget/history/retrieval settings with startup checks | C# + appsettings |
| Tests | Unit + integration coverage for the above | xUnit projects |
| Documentation | Runtime-flow and feature docs updated | Markdown |

## Optional Outputs
- Per-turn admission trace surface reserved for Phase 08 diagnostics integration.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
