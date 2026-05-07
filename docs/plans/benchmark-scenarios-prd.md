# PRD: Benchmark Scenarios and Reproducible Metrics

## Overview

Define and publish a benchmark suite for LeanKernel covering support triage, research synthesis, and coding tasks, with reproducible datasets, scoring rubrics, and tracked performance metrics over time.

## Problem Statement

Without standardized benchmarks, improvements are difficult to verify and compare. Teams need objective evidence of quality, speed, reliability, and cost impact.

## Goals

- Establish repeatable benchmark scenarios that map to target audience workflows.
- Measure quality, latency, reliability, and cost in a consistent framework.
- Track trendlines for regressions and improvements across releases.
- Produce stakeholder-friendly evidence for product and GTM narratives.

## Non-Goals (v1)

- Public leaderboards across external projects.
- Human-only grading for every run.
- Benchmarking every possible tool permutation.

## Benchmark Scope

| Scenario | Description | Primary Value Metric |
| -------- | ----------- | -------------------- |
| Support triage | Classify issue, propose next actions, and escalation correctness | Resolution quality score |
| Research synthesis | Gather, summarize, and cite multi-source findings | Accuracy + citation quality |
| Coding task | Multi-file change planning and patch quality | Pass rate on test/eval harness |

## User Stories

- As product, I can prove that new changes improved outcomes.
- As engineering, I can catch regressions before release.
- As GTM, I can cite concrete performance claims.
- As operations, I can compare quality-cost tradeoffs by profile.

## Functional Requirements

### FR-1 Dataset Versioning

- Store benchmark datasets under versioned manifest.
- Include prompt, expected rubric, and reference artifacts.
- Support pinned dataset IDs in CI.

### FR-2 Deterministic Execution Harness

- Execute benchmark runs with fixed config profile and seed controls where possible.
- Capture run metadata: model routes, budget profile, autonomy policy mode.

### FR-3 Scoring Framework

Scoring dimensions:

- task success
- factual accuracy
- constraint coverage
- citation quality (where applicable)
- latency and cost

Use weighted aggregate score per scenario.

### FR-4 Regression Detection

- Compare candidate run vs baseline window.
- Flag regressions exceeding thresholds.
- Block release gates when critical regressions occur.

### FR-5 Reporting

- Produce machine-readable JSON report.
- Produce markdown summary for PRs/releases.
- Plot historical trendlines by scenario and version.

### FR-6 Reproducibility Requirements

- Persist config snapshot with every benchmark run.
- Include dataset version hash and code commit SHA.

## Non-Functional Requirements

- Full benchmark suite runtime <= 60 minutes in CI nightly mode.
- Smoke benchmark runtime <= 10 minutes in PR mode.
- Scoring services availability >= 99.9% in scheduled pipeline.

## Architecture

| Component | Responsibility |
| --------- | -------------- |
| `BenchmarkManifest` | Dataset and rubric versioning |
| `BenchmarkRunner` | Scenario execution orchestration |
| `BenchmarkScorer` | Scoring and aggregation |
| `RegressionGate` | Threshold checks and release decisions |
| `BenchmarkReporter` | JSON/markdown/trend output |

## Data Model

### Benchmark Case

```json
{
  "id": "coding_014",
  "scenario": "coding",
  "input": "Refactor route selector for fallback reason logging",
  "rubric": {
    "mustInclude": ["reason code", "tests"],
    "weights": {
      "success": 0.4,
      "quality": 0.3,
      "latency": 0.15,
      "cost": 0.15
    }
  },
  "datasetVersion": "v1.2.0"
}
```

### Run Result

```json
{
  "runId": "bm_2026_05_07_001",
  "caseId": "coding_014",
  "score": 0.82,
  "latencyMs": 15420,
  "estimatedUsd": 0.036,
  "status": "pass",
  "commitSha": "abc1234"
}
```

## CI/CD Requirements

- PR pipeline:
  - run smoke subset
  - fail on severe regressions
- Nightly pipeline:
  - run full suite
  - publish trend artifacts
- Weekly report:
  - scenario deltas and top regressions/improvements

## Success Metrics

- benchmark pass rate per scenario
- aggregate score trend by release
- p95 latency trend
- cost-per-success trend
- regression count per month

## Rollout Plan

1. Phase 0: define manifests and scoring rubric.
2. Phase 1: implement runner and smoke suite in CI.
3. Phase 2: nightly full suite and trend dashboards.
4. Phase 3: release gating and public-facing summary metrics.

## Acceptance Criteria

- AC-1: Benchmark datasets are versioned and immutable once published.
- AC-2: Same commit + same dataset + same config produces materially consistent scores.
- AC-3: PR smoke suite detects intentionally introduced regression cases.
- AC-4: Nightly reports include quality, latency, and cost trends per scenario.
- AC-5: Release gate blocks when critical thresholds are violated.

## Dependencies

- Run replay and telemetry pipeline for consistent metadata capture.
- Budget and autonomy policy configs for controlled test profiles.
- Existing unit/integration test infrastructure.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Benchmark drift from real-world use | Quarterly scenario refresh with user-feedback inputs |
| Overfitting to benchmark prompts | Hidden holdout set and rotating challenge cases |
| Flaky external dependencies | Mocked/frozen fixtures for deterministic subset |

## Open Questions

- Which metrics should become release blockers in v1?
- Should human review be required for top-tier benchmark claims?
- How frequently should benchmark datasets be refreshed?

## Implementation Clarifications (v1 Defaults)

Use these defaults unless product or release engineering review overrides them during grooming:

- Benchmark manifests and datasets are stored in-repo, versioned, and immutable once published. Corrections create a new version instead of mutating an old one.
- PR smoke runs include at least one case per scenario. Nightly runs execute the full suite plus hidden holdout cases.
- Reproducibility snapshots must include commit SHA, benchmark dataset version, route profile, autonomy mode, budget profile, and any seed or prompt-template controls.
- Human review is required for externally published claims and for rubric calibration changes, but not for every routine CI benchmark run.
- v1 release blockers are critical regressions in coding pass rate, support triage quality score, or p95 cost-per-success beyond agreed thresholds.

## Sprint-Ready Engineering Tickets

- [ ] `BMK-01` Define the benchmark manifest, rubric, and run-result schemas, plus the repo layout and versioning rules for benchmark datasets and reference artifacts.
- [ ] `BMK-02` Implement the deterministic benchmark runner that captures config snapshots, execution artifacts, route metadata, and commit-linked outputs.
- [ ] `BMK-03` Build scenario scorers and weighted aggregation logic for support triage, research synthesis, and coding tasks with golden test fixtures.
- [ ] `BMK-04` Add PR smoke and nightly full-suite CI workflows that publish JSON artifacts, markdown summaries, and baseline comparison outputs.
- [ ] `BMK-05` Implement the regression gate with configurable thresholds, holdout support, and clear failure messaging for pull requests and release candidates.
- [ ] `BMK-06` Build trend reporting outputs for scenario scores, latency, cost-per-success, and regression counts so weekly reporting is generated automatically.
- [ ] `BMK-07` Create the initial benchmark corpus, establish the first baseline run, and document the refresh cadence and claim-governance process for future updates.
