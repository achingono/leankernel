# Ambiguity and Reference-Resolution Implementation Plan

## Problem and approach

The current ambiguity pipeline handles mainly person-name collisions but is weak on relation/pronoun grounding and cross-source ambiguity. The implementation will proceed in two tracks: (1) immediate threshold hardening to reduce overconfident assertions now, then (2) structural upgrades for typed reference resolution and generalized disambiguation.

## Current state (codebase baseline)

- `ContextGatekeeper.BuildDisambiguationHints` is person-name focused (`EntityHintType.Person`, multi-match only).
- `EntityHintExtractor` emits relationship terms as `Person` and pronouns via a single-candidate carry-over path.
- `ResolveRecentPerson` returns one historical antecedent, which cannot model multi-antecedent pronouns.
- Fallback/disambiguation thresholds are global and already configurable (`Settings`, `ConfigController`, runtime config).
- `WikiStore.RankCandidates` includes broad `factKeys` in the haystack, which can overweight secondary references.

## Mapping/collision scenario matrix (inventory + solution + test layer)

1. **Exact person-name collision** (`John` -> multiple people)  
   **Solution:** score-gap disambiguation with confidence policy.  
   **Test layer:** Unit (`ContextGatekeeperFullTests`).

2. **Alias collision** (nickname/shortname -> multiple entries)  
   **Solution:** alias-cluster resolution + confirm when score gap is weak.  
   **Test layer:** Unit.

3. **Relationship-token collision** (`mother`, `brother`) across aggregate and person entries  
   **Solution:** relationship-slot grounding before final candidate selection.  
   **Test layer:** Unit + Integration.

4. **Pronoun -> single clear antecedent** (`him`, `her`)  
   **Solution:** deterministic antecedent carry-over with confidence floor.  
   **Test layer:** Unit.

5. **Pronoun -> multiple antecedents** (`my mother... and my brother...`, then `her/him/them`)  
   **Solution:** multi-candidate antecedent set + forced disambiguation when unresolved.  
   **Test layer:** Unit.

6. **Pronoun/relationship mismatch** (pronoun target conflicts with prior role mention)  
   **Solution:** mismatch penalty + clarify.  
   **Test layer:** Unit.

7. **Person vs organization token collision**  
   **Solution:** typed-hint conflict detection + intent confirmation.  
   **Test layer:** Unit.

8. **Wiki-vs-document collision** (conflicting high candidates across sources)  
   **Solution:** source-disagreement ambiguity signal + clarify when unresolved.  
   **Test layer:** Unit + Integration.

9. **Single weak match without explicit collision**  
   **Solution:** low-confidence ambiguity rule (clarify instead of asserting).  
   **Test layer:** Unit.

10. **Stale-vs-recent fact collision**  
    **Solution:** recency-aware confidence policy + uncertainty prompt when needed.  
    **Test layer:** Unit.

11. **Plural/group ambiguity** (`they/them`, `parents`, `siblings`)  
    **Solution:** grouped-candidate disambiguation.  
    **Test layer:** Unit.

12. **Elliptical follow-up references** (`and my brother too`, `what do you know about her?`)  
    **Solution:** conversational reference-frame reuse with confidence gating.  
    **Test layer:** Unit + Integration.

## Revised phased implementation plan

### Phase 1: Immediate threshold hardening (start now)

- Apply stricter defaults (in `appsettings` + config surface):

| Config key | Current | Proposed |
|---|---:|---:|
| `Context.LowConfidenceFallbackThreshold` | 0.72 | 0.80 |
| `Context.AmbiguityLowConfidenceThreshold` | 0.78 | 0.85 |
| `Context.AmbiguityConfidenceGapThreshold` | 0.10 | 0.15 |

- Keep runtime configurability via Settings/API unchanged.
- Add tests proving stricter defaults trigger fallback/disambiguation on short ambiguous queries.

**Phase 1 acceptance**
- Config read/write surfaces expose the new defaults.
- Ambiguous low-confidence queries produce disambiguation hints or fallback recall under test.

### Phase 2: Typed reference model + extraction (prerequisite phase)

- Extend `EntityHintType` with `Relationship` and `Pronoun`.
- Update relationship extraction to emit `EntityHintType.Relationship` (not `Person`).
- Update pronoun extraction to emit `EntityHintType.Pronoun`.
- Replace single-return antecedent function with multi-candidate antecedent resolution (confidence per candidate).
- Update gatekeeper dimension-routing logic to incorporate new hint types.

**Phase 2 acceptance**
- Extractor returns typed hints for relationship/pronoun inputs.
- Multi-antecedent history produces >1 pronoun candidate where expected.

### Phase 3: Generic collision/ambiguity classifier

- Build cross-type ambiguity classifier consuming Phase 2 signals.
- Include ambiguity signals: top score, score gap, source disagreement, recency conflict, and single-weak-match condition.
- Enforce global policy: low-confidence relation/pronoun references must require clarification before identity assertion.

**Phase 3 acceptance**
- Classifier emits ambiguity decisions for scenarios 1-12 using typed hints.

### Phase 4: Disambiguation contract expansion

- Expand `BuildDisambiguationHints` to handle person, alias, relationship, pronoun, plural/group, and single-weak cases.
- Standardize hint templates (reason, top candidates, required confirmation action).
- Preserve current strong-winner behavior (skip disambiguation when clearly confident).

**Phase 4 acceptance**
- Disambiguation hints are generated for each applicable scenario class.
- Existing high-confidence winner tests still pass.

### Phase 5: Retrieval/index role-safe tuning

- Rebalance ranking inputs to reduce over-weighting of broad `factKeys` in relation-sensitive queries.
- Add role-aware boosts/penalties to prefer role-specific entries over aggregate entries when evidence supports that.
- Add source-shape handling for aggregate-vs-specific entry ties.

**Phase 5 acceptance**
- In controlled tests, relationship-sensitive queries rank role-specific candidate above aggregate candidate.
- Existing name-collision retrieval behavior does not regress.

### Phase 6: Verification matrix and regression suite

- Build table-driven tests for all 12 scenarios with explicit layer tags (`Unit`/`Integration`).
- Extend:
  - `ContextGatekeeperFullTests`
  - `EntityHintExtractorTests`
  - `ContextCandidateRetrieverTests` (exists; expand with scenario matrix cases)
  - relevant config/controller tests for threshold defaults and patching

**Phase 6 acceptance**
- Each scenario has at least one deterministic automated test and mapped expected outcome.

## TODOs for execution tracking

1. `threshold-hardening-profile`: apply threshold defaults, config assertions, and low-confidence trigger tests.
2. `typed-reference-model`: add `Relationship`/`Pronoun` hint types and extraction outputs.
3. `antecedent-candidate-resolution`: replace single antecedent carry-over with candidate-set resolution.
4. `ambiguity-classifier-generalization`: implement cross-type ambiguity scoring and decisions.
5. `disambiguation-contract-expansion`: generate clarifying hints across all ambiguity classes.
6. `retrieval-role-safety-tuning`: implement role-aware ranking safeguards and acceptance tests.
7. `scenario-regression-suite`: deliver full 12-scenario test matrix.

## Notes and decisions

- Scope is generic and reusable; no family-specific hardcoding.
- Confirmed behavior policy: **always ask clarification before asserting identity when relation/pronoun confidence is low**.
- Phase 1 is independently executable immediately; Phases 2-5 are sequenced to avoid rework.
