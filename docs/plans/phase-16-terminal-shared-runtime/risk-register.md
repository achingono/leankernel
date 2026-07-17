# Phase 16 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Shared abstraction accidentally changes terminal behavior | Message flow regressions | Keep helper methods behavior-preserving and run targeted builds/tests | Open |
| R2 | Over-generalization makes channel code harder to follow | Maintenance cost | Extract only proven duplicates with simple APIs | Open |
| R3 | Gateway coupling to terminal shared project creates dependency sprawl | Architecture drift | Keep shared project low-level and helper-only (no transport/domain coupling) | Open |

## Open Decisions
- Whether to include markdown/style parsing in shared response extraction now or in follow-up phase.
