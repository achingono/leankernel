# Phase 23 Exit Criteria

## Gate Checklist
- [ ] Golden memory/retrieval fixture sets are versioned and reproducible.
- [ ] Replay harness produces deterministic outputs for fixed snapshots.
- [ ] Quality metrics (recall, grounding, freshness, contradiction leakage) are computed per run.
- [ ] CI gates fail on regression beyond configured thresholds.
- [ ] Baseline refresh and threshold-change process is documented and reviewed.
- [ ] Integration tests validate gating behavior and reporting artifacts.

## Initial Promotion Thresholds (v1)

- [ ] **Retrieval recall@5** on golden fixture set is **>= 0.85** overall and **>= 0.80** per protected scenario class.
- [ ] **Grounded-answer rate** is **>= 0.90** overall and does not regress by more than **2.0 percentage points** versus current baseline.
- [ ] **Freshness lag p95** for freshness-sensitive scenarios is **<= 24h** equivalent event age.
- [ ] **Contradiction leakage rate** (responses presenting conflicting claims as settled truth) is **<= 1.0%**.
- [ ] **False-positive conflict flag rate** in truth-lifecycle evaluations is **<= 5.0%**.
- [ ] **Determinism check**: identical replay inputs produce identical scored outputs with **100% hash match** across two consecutive runs.

## Threshold Governance

- [ ] Thresholds are stored in versioned config and applied by CI in fail-closed mode.
- [ ] Any threshold reduction requires explicit owner + reviewer approval and evidence of low-risk impact.
- [ ] Baseline refresh cadence and ownership are documented, with drift review before promoting new baselines.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
