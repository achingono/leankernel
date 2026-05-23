# Reference Discovery Useful-by-Default PRD

## Background and problem

Recent improvements increased context retrieval quality, but a broader gap remains: the assistant still misses **buried references** when users speak implicitly, elliptically, or with partial mentions.

Two recent misses are examples, not special cases:

- ambiguous named references requiring repeated user clarification,
- implicit family references where relevant grief context existed in wiki but was not surfaced.

The underlying issue is generic: retrieval and scoring over-favor obvious lexical overlap and under-represent semantically relevant, relationship-rich facts stored in deeper wiki structure and document knowledge.

## Core objective

Make the assistant **useful by default** for any domain when a user references people, organizations, events, or relationships indirectly.

Success means the first response should usually include the right grounding without requiring multi-turn “digging” by the user.

## Design principles

1. **Domain-agnostic by default**: no hardcoded logic for work/family-specific scenarios.
2. **Data-driven relevance**: prefer generalized feature signals (reference type, relation depth, confidence, recency) over case-specific heuristics.
3. **Bounded recall**: increase contextual coverage without uncontrolled context bloat.
4. **Transparent uncertainty**: when confidence is low or identity is ambiguous, ask targeted clarification while still providing best-guess grounding.
5. **Explainability**: every include/exclude decision must be inspectable in diagnostics.

Unclear-query trigger contract for this plan:

- low confidence in top candidates,
- ambiguous top hits,
- weak lexical overlap in first-pass retrieval.

## Scope

### In scope

- Generalized reference-hint extraction from message + recent turns:
  - explicit names,
  - role/relationship noun phrases,
  - pronoun/anaphora carry-over,
  - indirect event anchors.
- Wiki candidate generation that uses full evidence surface:
  - subject, summary, aliases, tags, claims, and structured fact context.
- Document knowledge candidate generation that applies the same reference-aware discovery policy used for wiki.
- Relation-aware expansion with bounded depth for supporting context.
- Selection strategy updates for primary vs supporting reference context.
- Ambiguity and uncertainty contract for prompt assembly.
- Telemetry, diagnostics, and benchmark scenarios spanning multiple domains.
- Config controls for tuning retrieval breadth and thresholds.

### Out of scope

- Full schema redesign of Qdrant/indexer pipeline.
- Agent behavior changes unrelated to context discovery.
- UI polish beyond required config/observability surfaces.

## Generic root-cause categories

1. **Surface mismatch**: user query tokens do not match indexed tokens for relevant facts.
2. **Dimension skew**: classifier over-weights one dimension and suppresses semantically central dimensions.
3. **Sparse-candidate dominance**: short lexical matches outrank richer but less literal entries.
4. **Threshold brittleness**: globally tuned thresholds drop high-value supporting context.
5. **Ambiguity handling gaps**: multiple plausible entities/events are not represented as ranked hypotheses.

## Architecture and behavior plan

### 1. Generalized Reference Hint Layer

**Deliverables**

1. Introduce/extend a hint model that supports:
   - `ReferenceKind` (Person, Organization, Relationship, Event, Unknown),
   - `SignalType` (explicit, anaphoric, role-based, contextual),
   - `Confidence`, `SourceTurn`, and optional `AnchorTerms`.
2. Replace hardcoded extractor alias maps with data-driven matching over wiki/document aliases and tags (no embedded org/person name lists in extraction logic).
3. Expand extraction beyond proper names to include relationship and role references.
4. Add bounded conversational carry-forward for unresolved references.

### 2. Rich Candidate Surface and Indexing (Wiki + Documents)

**Deliverables**

1. Ensure ranking/matching surface includes structured fact context fields, not claim text only.
2. Add normalized “retrieval text” projections for both wiki entries and document chunks for consistent token overlap and explainability.
3. Preserve source confidence/provenance weighting in candidate scoring.
4. Add a second-pass **deprioritized recall search** when the first pass is low-confidence or ambiguous, so hidden references can still be discovered.
5. Fallback behavior contract: run dual-source (wiki + document) recall with expanded evidence surface and relaxed ranking before giving up on discovery.

### 3. Relation-Aware Expansion (Bounded)

**Deliverables**

1. Expand from high-confidence seed candidates to related entries with configurable depth (explicitly iterate configured depth; no implicit one-hop behavior).
2. Differentiate candidate roles:
   - **Primary** (directly referenced),
   - **Supporting** (relation-neighbor evidence),
   - **Ambient** (lower-priority background context).
3. Candidate-role mapping contract:
   - Primary -> `ContextPriority.High`
   - Supporting -> `ContextPriority.Low`
   - Ambient -> `ContextPriority.Medium`
4. Add safeguards to prevent unrelated expansion spillover.

### 4. Selection Strategy and Thresholding

**Deliverables**

1. Move from one-size threshold to role-aware thresholds:
   - stricter for ambient,
   - lower for supporting if attached to strong primary.
2. Make all boost values config-bound (remove hardcoded scoring constants from retrieval/selection path).
3. Add continuity signal from recent-turn hints to reduce dropped references across turns.
4. Keep strict token budget enforcement with explicit exclusion reasons.

### 5. Ambiguity + Uncertainty Contract

**Deliverables**

1. Emit ranked hypothesis sets for ambiguous references across wiki and document candidates.
2. Prompt assembler behavior:
   - present best-supported hypothesis context first,
   - include concise disambiguation question when confidence is below threshold and multiple plausible hits exist.
3. Ensure non-blocking behavior: uncertainty should not force empty context when useful evidence exists.
4. For 2+ plausible low-confidence hits, return best-guess grounding plus a concise disambiguation prompt in the same response.

### 6. Observability, Benchmarks, and Rollout Safety

**Deliverables**

1. Add diagnostics events for:
    - hints extracted,
    - candidate roles,
    - threshold decisions,
    - first-pass vs deprioritized-pass source,
    - ambiguity triggers.
2. Build benchmark suite from real threads across multiple domains (work, family, scheduling, identity, goals), with each domain represented by multiple scenarios.
3. Lock and publish baseline benchmark artifacts **before implementation**.
4. Add regression gates for recall/precision deltas before rollout.
5. Document tuning controls and operator playbook.

## Acceptance criteria (generic and measurable)

1. **Baseline lock**: benchmark suite artifacts are captured on the pre-change baseline and linked in CI before implementation rollout starts.
2. **Implicit-reference recall**: Top-3 recall for buried references reaches **>= 0.80** across the benchmark suite.
3. **First-response usefulness**: average clarification turns for buried-reference scenarios is **<= 1.5**.
4. **Cross-domain robustness**: criteria 2 and 3 hold across at least 4 scenario classes; each class has at least 3 scenarios.
5. **Dual-source discovery**: in low-confidence unclear queries, fallback pass searches both wiki and documents and can surface previously deprioritized hits.
6. **Ambiguity handling**: when multiple plausible hits exist below confidence threshold, ranked hypotheses and targeted clarification prompt are produced.
7. **Precision guardrail**: irrelevant context injection increases by no more than 5% on control scenarios.
8. **Budget guardrail**: token budget adherence remains unchanged.
9. **Explainability**: every excluded high-salience candidate has machine-readable reason metadata.
10. **No hardcoded domain lists**: extractor path contains no hardcoded proper-name/org dictionaries.
11. **Regression safety**: explicit-reference scenarios do not degrade.
12. **Configurability**: all new scoring/expansion knobs are config-bound and documented.

## Key implementation surfaces

- `src/LeanKernel.Archivist/EntityHintExtractor.cs`
- `src/LeanKernel.Archivist/ContextGatekeeper.cs`
- `src/LeanKernel.Archivist/ContextCandidateRetriever.cs`
- `src/LeanKernel.Archivist/LeanKernelSelectionStrategy.cs`
- `src/LeanKernel.Archivist/Wiki/*` (indexing/query surface and ingestion quality)
- `src/LeanKernel.Thinker/PromptAssembler.cs`
- `src/LeanKernel.Core/Models/*` and `src/LeanKernel.Core/Interfaces/*`
- `src/LeanKernel.Tests.Unit/Archivist/*` + scenario benchmark fixtures

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Overfitting to recent misses | Enforce multi-domain benchmark coverage and ban case-specific branching in acceptance review |
| Context bloat | Relation-depth caps, role-aware thresholds, strict budget accounting |
| False positives from broad hints | Confidence weighting + precision regression suite |
| Hardcoded domain alias drift | Replace static alias dictionaries with data-driven alias/tag sources and test for absence of hardcoded name maps |
| Ambiguity fatigue | Trigger disambiguation only above ambiguity threshold; keep prompt concise |
| Operational opacity | Structured diagnostics + per-turn retrieval trace |

## Review and implementation gate

Implementation starts only after:

1. independent cross-model plan review is complete,
2. review feedback is integrated,
3. acceptance criteria are approved as domain-agnostic and measurable.
