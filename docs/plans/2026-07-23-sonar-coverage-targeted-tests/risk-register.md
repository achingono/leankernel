# Sonar Coverage Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Added tests do not move gate enough due to broad new-code window | Rework cycle and delayed completion | Prioritize files with highest uncovered lines first; re-measure after each batch | Open |
| R2 | File-system or multipart tests are flaky in CI/container runs | Unreliable quality signal | Use deterministic temp directories and explicit request setup; avoid time-based assertions | Open |
| R3 | Sonar scan runtime is long and masks quick iteration feedback | Slower delivery | Run targeted test projects first, then run full Sonar once behavior is stable | Open |
| R4 | Sonar new-code period shifts or includes unrelated changes | Coverage target moves during implementation | Re-check `project_status` and `component_tree` immediately before final scan; document snapshot used | Open |
| R5 | Coverage report import mismatch causes under-reporting | False quality gate failures | Keep OpenCover paths unchanged and verify scanner logs include imported coverage report statistics | Open |

## Open Decisions
- Whether one additional hotspot (`GBrainDocumentStoreClient`) needs tests after gateway/middleware coverage improvements.
