# Phase 23 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Non-deterministic replay due to external dependencies | Unstable CI signal | Stub/provider-free replay mode and pinned fixtures | Open |
| R2 | Metrics become noisy or not actionable | False gates or missed regressions | Calibrate thresholds with rolling baseline and confidence intervals | Open |
| R3 | Fixture drift from production reality | Overfit test suite | Refresh cadence with sampled telemetry-backed fixtures | Open |
| R4 | High eval runtime cost in CI | Slower pipeline and reduced adoption | Tiered fast/slow suites and nightly full replay | Open |

## Open Decisions
- Which metrics are hard gates vs advisory-only.
- Baseline update approval workflow and ownership.
