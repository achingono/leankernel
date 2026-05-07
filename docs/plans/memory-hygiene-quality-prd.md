# PRD: Memory Hygiene and Quality Scoring Pipelines

## Overview

Implement automated memory hygiene jobs and quality scoring for wiki facts and session history to reduce stale, contradictory, or low-value memory while improving retrieval precision.

## Problem Statement

As memory volume grows, retrieval quality can degrade due to duplication, contradiction, stale facts, and noisy context. This increases token waste and reduces answer relevance.

## Goals

- Continuously score memory quality and freshness.
- Detect and flag contradictions and duplicates.
- Propose or execute safe cleanup actions.
- Improve retrieval precision and context efficiency.

## Non-Goals (v1)

- Fully autonomous deletion of high-confidence records without safeguards.
- Domain-specific truth verification using external fact APIs.
- Multi-language semantic normalization beyond English-first support.

## User Stories

- As an operator, I can see which memory entries are stale or low quality.
- As an admin, I can approve merge/archive/delete recommendations.
- As the system, I can prioritize higher-quality memory during retrieval.
- As a user, I experience fewer contradictory responses over time.

## Functional Requirements

### FR-1 Memory Quality Score

Compute score 0.0 to 1.0 for each memory unit using weighted factors:

- freshness decay
- source confidence
- usage/access frequency
- contradiction penalty
- duplication penalty

### FR-2 Contradiction Detection

- Detect probable conflicts between entries sharing entity/topic overlap.
- Attach contradiction links and confidence score.
- Require reviewer confirmation before destructive action.

### FR-3 Duplicate and Near-Duplicate Detection

- Use embedding similarity and canonical key heuristics.
- Create merge candidates with suggested canonical record.

### FR-4 Hygiene Actions

Supported actions:

- `archive_candidate`
- `merge_candidate`
- `refresh_candidate`
- `delete_candidate` (only low-confidence + low-usage + stale)

### FR-5 Retrieval Integration

- Retrieval ranker incorporates memory quality score.
- Low-scoring memory deprioritized unless no alternatives exist.

### FR-6 Scheduled Pipelines

- Daily lightweight scoring pass.
- Weekly deep hygiene pass (duplicate and contradiction analysis).
- On-demand manual run from admin UI.

## Non-Functional Requirements

- Scoring pipeline should complete daily pass in <= 15 minutes for 100k records.
- Deep pass should be resumable and checkpointed.
- No blocking impact on online query latency.

## Architecture

| Component | Responsibility |
| --------- | -------------- |
| `MemoryScorer` | Computes quality/freshness metrics |
| `ContradictionDetector` | Detects conflicting facts |
| `DuplicateDetector` | Finds merge clusters |
| `HygienePlanner` | Produces actionable recommendations |
| `HygieneExecutor` | Applies approved actions |
| `HygieneScheduler` | Orchestrates daily/weekly jobs |

## Data Model

### Memory Quality Record

```json
{
  "memoryId": "who-alice-smith",
  "qualityScore": 0.78,
  "freshnessScore": 0.64,
  "duplicationScore": 0.9,
  "contradictionScore": 0.1,
  "lastScoredAt": "2026-05-07T02:00:00Z"
}
```

### Hygiene Recommendation

```json
{
  "id": "rec_101",
  "type": "merge_candidate",
  "primaryMemoryId": "what-project-atlas",
  "relatedMemoryIds": ["what-atlas-project"],
  "confidence": 0.88,
  "status": "pending_review"
}
```

## API Requirements

| Method | Endpoint | Purpose |
| ------ | -------- | ------- |
| GET | `/api/memory/quality` | List scored memory records |
| GET | `/api/memory/hygiene/recommendations` | List pending recommendations |
| POST | `/api/memory/hygiene/recommendations/{id}/approve` | Approve action |
| POST | `/api/memory/hygiene/recommendations/{id}/dismiss` | Dismiss action |
| POST | `/api/memory/hygiene/run` | Trigger on-demand pipeline |

## UI Requirements

- New admin view: `Memory Quality` with filters by score band, dimension, and status.
- Recommendation inbox with merge preview and contradiction diff.
- Trend chart: average memory quality score over time.

## Safety Requirements

- Never auto-delete entries with confidence >= configured threshold.
- Keep reversible action log and restore path.
- Require explicit approval for delete and merge actions in v1.

## Telemetry and Success Metrics

- `memory_quality_avg`
- `contradictions_detected_total`
- `duplicate_clusters_total`
- `hygiene_actions_applied_total`
- `retrieval_precision_at_k` (pre/post)

## Rollout Plan

1. Phase 0: score-only mode, no actions.
2. Phase 1: recommendation generation and UI review.
3. Phase 2: approval-based merge/archive actions.
4. Phase 3: retrieval ranking integration.

## Acceptance Criteria

- AC-1: 100% of memory entries receive quality scores on schedule.
- AC-2: Contradiction and duplicate recommendations show confidence and evidence.
- AC-3: Approved actions are reversible within retention window.
- AC-4: Retrieval precision-at-5 improves by >= 10% on benchmark suite.
- AC-5: Token usage for memory payload decreases by >= 15% without quality regression.

## Dependencies

- Existing wiki/session storage schema.
- Embeddings pipeline and vector similarity services.
- Admin review UI framework.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| False positives in contradiction detection | Confidence thresholds + human review |
| Over-pruning useful historical context | Archive-first strategy, not immediate delete |
| Pipeline cost growth | Incremental scoring and changed-record prioritization |

## Open Questions

- Should contradiction checks be dimension-specific in v1?
- What confidence threshold should auto-archive use in v2?
- How long should reversible action snapshots be retained?

## Implementation Clarifications (v1 Defaults)

Use these defaults unless retrieval or operations review overrides them during grooming:

- A memory unit is any retrievable wiki fact, session summary chunk, or similar persisted record with a stable ID and source metadata.
- The daily scoring pass is incremental over changed or newly created records. The weekly deep pass can expand to related neighbors for contradiction and duplicate analysis.
- v1 never auto-applies destructive actions. Archive and merge remain approval-based, and delete stays recommendation-only until post-v1 evidence supports it.
- Retrieval uses quality score as a ranking modifier rather than a hard filter so low-scoring memory can still surface when no better alternative exists.
- Recommendation evidence must include the top contributing signals, related record IDs, and preview excerpts so reviewers can act without opening raw storage.

## Sprint-Ready Engineering Tickets

- [ ] `MHQ-01` Define the quality score schema, weighting configuration, recommendation model, and reversible action snapshot format.
- [ ] `MHQ-02` Implement the incremental `MemoryScorer` daily job with checkpoints, freshness decay, usage weighting, and score persistence.
- [ ] `MHQ-03` Implement contradiction and duplicate detectors that output confidence, evidence payloads, and linked record references suitable for human review.
- [ ] `MHQ-04` Build the hygiene planner and approval-based executor for archive and merge actions, including restore flows and audit history.
- [ ] `MHQ-05` Integrate memory quality as a secondary ranking feature in retrieval behind a feature flag and validate that online latency remains unaffected.
- [ ] `MHQ-06` Add admin APIs and UI for score bands, pending recommendations, merge previews, contradiction diffs, and trend reporting.
- [ ] `MHQ-07` Extend benchmark and integration coverage to prove score coverage, reversibility, retrieval precision improvement, and memory payload token reduction targets.
