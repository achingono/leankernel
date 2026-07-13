# ADR 0001: Adopt a MAF-native modular monolith

- Status: Accepted
- Date: 2026-07-13

## Context

After commit `c9cd93b68d1d6b439661cff799c258f079c62c37`, both Copilot and OpenCode sessions converged on the same conclusion: this rebuild should use as much of Microsoft Agent Framework (MAF) as possible and should be implemented against the repository's actual four-project layout rather than the more aspirational structure described in `README.md`.

The logs show an early correction from "stored memory" about the solution shape to the real repository structure:

- `src/Common/LeanKernel.Core`
- `src/Common/LeanKernel.Data`
- `src/Common/LeanKernel.Logic`
- `src/Services/LeanKernel.Gateway`

The design work repeatedly emphasized that custom code should exist only where MAF needs durable persistence, identity scoping, or integration-specific glue.

## Decision

LeanKernel will use a modular-monolith architecture with four primary projects and a MAF-native runtime model.

- `LeanKernel.Gateway` is the composition root and HTTP host.
- `LeanKernel.Logic` contains reusable runtime logic and MAF-facing providers.
- `LeanKernel.Data` owns EF Core persistence.
- `LeanKernel.Core` owns core entities and contracts.

The runtime will prefer MAF primitives over custom abstractions, especially:

- `AIAgent` and named agent registration
- `AgentSession` and `AgentSessionStore`
- `ChatHistoryProvider`
- `AIContextProvider`
- OpenAI-compatible hosted endpoints

Custom code is limited to the pieces MAF does not provide directly:

- persistence adapters
- identity/partition resolution
- GBrain integration glue
- request-scoped permit/session isolation handling

## Consequences

Positive:

- The runtime stays aligned with upstream MAF extension points instead of inventing a parallel framework.
- The codebase has a clear composition root and reusable common libraries.
- Future features can build on stable MAF concepts instead of custom lifecycle semantics.

Tradeoffs:

- Gateway must absorb more framework-specific wiring.
- Existing README aspirations must be treated as roadmap, not current architecture.
- Some design choices are constrained by MAF lifetimes and provider contracts.

## Evidence From Session Logs

- Copilot session `4fea4147-979d-40a2-a059-f64bcbdcb0a3`, `2026-07-12`, "Create PRD for LeanKernel Rebuild"
  - User requested a rebuild that leverages as much native MAF as possible.
  - The session corrected the architecture to the actual four-project structure and built the initial PRD around MAF-native components.
- OpenCode session `ses_0abffac42ffeqKxiSWd1Vg90bD`, `2026-07-12`, "PRD implementation and architecture gap review"
  - The review tightened the plan around MAF primitives, named agents, durable providers, and the real project boundaries.
