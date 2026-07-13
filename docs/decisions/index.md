# Architectural Decision Records

This directory captures architectural decisions reconstructed from OpenCode session logs in `~/.local/share/opencode` and Copilot session logs in `~/.copilot/session-state` for this repository since commit `c9cd93b68d1d6b439661cff799c258f079c62c37` (`2026-07-10T11:27:45-04:00`).

These ADRs document decisions that repeatedly appeared during planning and implementation and that still match the current repository structure, PRDs, and code boundaries.

## ADRs

- [0001 - Adopt a MAF-native modular monolith](0001-adopt-a-maf-native-modular-monolith.md)
- [0002 - Partition runtime state by persisted tenant, user, and channel identities](0002-partition-runtime-state-by-persisted-identities.md)
- [0003 - Separate transcript sessions from durable agent runtime state](0003-separate-transcript-sessions-from-agent-runtime-state.md)
- [0004 - Keep GBrain transport in Gateway and Logic provider-agnostic](0004-keep-gbrain-transport-in-gateway.md)
- [0005 - Use a deterministic-first 5W1H memory pipeline with bounded small-model refinement](0005-use-a-deterministic-first-5w1h-memory-pipeline.md)
