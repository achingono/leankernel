# PRD - Import 5W1H Memory Logic into LeanKernel.Logic

| | |
|---|---|
| **Status** | In progress - core pipeline implemented in `LeanKernel.Logic`; remaining gaps tracked in checklist |
| **Owner** | @achingono |
| **Target area** | `src/Common/LeanKernel.Logic` |
| **Source repo** | `~/source/repos/leankernel` |
| **Primary source files** | `src/LeanKernel.Scheduler/JobExecutor.cs`, `src/LeanKernel.Learning/FactExtractionStep.cs`, `docs/plans/knowledge-fact-defrag-job.md` |
| **Note** | The source repo contains `LeanKernel.Learning` with `FactExtractionStep.cs`; the rebuilt architecture consolidates all memory logic into `LeanKernel.Logic`. Fact extraction from conversation turns is **in-scope** for porting into `LeanKernel.Logic` as a new service. |

---

## 1. Summary

`LeanKernel.Logic` currently supports memory only as unstructured text blobs retrieved through `IMemoryClient` and injected into prompt context by `MemoryProvider`. The original repo already contains a stronger memory-page shape built around 5W1H normalization:

- `Who`
- `What`
- `When`
- `Where`
- `Why`
- `How`

That logic lives today inside scheduler-oriented code in `~/source/repos/leankernel/src/LeanKernel.Scheduler/JobExecutor.cs`. It is useful, but it is in the wrong place for the rebuilt runtime and is mixed with maintenance-job concerns such as retirement planning and job execution bookkeeping.

**Additionally, the source repo's `LeanKernel.Learning/FactExtractionStep.cs` extracts facts from conversation turns using an LLM and seeds `# Learned Fact` pages.** The rebuilt architecture consolidates all memory logic into `LeanKernel.Logic` — there is no separate `LeanKernel.Learning` project.

This PRD defines how to import and adapt the reusable 5W1H memory logic **and** the fact extraction capability into `src/Common/LeanKernel.Logic` so the runtime can:

1. **extract structured facts from conversation turns using an LLM** (`FactExtractionService`),
2. create structured memory pages from learned facts and other memory candidates,
3. normalize existing pages into a consistent 5W1H shape,
4. identify the key dimensions that best organize each page,
5. use a small LLM reasoning pass to improve dimension extraction and graph construction,
6. create deterministic and LLM-assisted cross links between related pages,
7. expose these pages to GBrain-backed storage and retrieval without duplicating scheduler behavior.

The intended result is that `LeanKernel.Logic` owns the page-shaping, dimension analysis, link-graph logic, **and fact extraction from conversation turns**, while transport and persistence remain in the existing memory client layer.

**Architectural split:**
- **LLM-required**: `FactExtractionService` (conversation → fact strings), optional small-model refinement passes (dimensions, graph edges, field repair)
- **Deterministic-first (works without LLM)**: `MemoryPageNormalizer`, `MemoryDimensionClassifier` (deterministic path), `MemoryPageLinker`, `MemoryPageRenderer`, `MemoryPageParser`

### Implementation snapshot (2026-07-12)

- Implemented in `src/Common/LeanKernel.Logic/Memory`: `FactExtractionService`, `MemoryPageParser`, `MemoryPageRenderer`, `MemoryPageNormalizer`, `MemoryDimensionClassifier`, `MemoryPageLinker`, `MemoryGraphReasoner`, `MemoryFieldRepairService`, `MemoryPageKeyBuilder`, `DisabledChatClient`, `ReasoningModel`, and core records.
- Integrated into `MemoryProvider` write/read flow in `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` (fact extraction -> normalization -> scope-relative save key; compact summary retrieval; fallback raw save on failure).
- Added model-tier configuration and keyed clients: `MemorySettings`, `FactExtractionSettings`, and DI registration in `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs`.
- Kept transport boundary intact: GBrain implementation remains in `src/Services/LeanKernel.Gateway/Providers`, while Logic only depends on `IMemoryClient` abstractions.
- Test status: memory-focused unit suites exist and pass; coverage for memory pipeline namespace exceeds 80% in latest runs.

---

## 2. Problem

### 2.1 Current runtime gap

Today the rebuilt repo has these limitations:

- `MemoryProvider` stores and retrieves plain text only.
- `IMemoryClient` has no first-class concept of a structured memory page.
- No logic exists in `LeanKernel.Logic` to normalize memory into 5W1H fields.
- No logic exists to determine which dimensions are most useful for organizing a page.
- No graph or cross-link model exists for connecting related memories.

### 2.2 Why importing the old logic directly is not enough

The source implementation is embedded in scheduler maintenance code and includes behavior that should not be imported as-is:

- scheduled defrag job orchestration,
- retirement planning and execution,
- job metrics and diagnostics persistence,
- direct dependence on the scheduler's agent runtime.

The rebuild needs the reusable logic extracted into runtime-friendly services that can be called during memory writeback, maintenance, and future UI/diagnostics flows.

---

## 3. Goals, Non-Goals, Scope, and Outcomes

### 3.1 Goals

- **G1 - Reuse the proven 5W1H page shape** from the source repo in `LeanKernel.Logic`.
- **G2 - Separate reusable logic from scheduler concerns** so the same normalization can run on writeback, repair, or batch maintenance.
- **G3 - Add dimension identification** so each page can be organized by its strongest dimensions, not just stored as free text.
- **G4 - Use a small LLM reasoning step for dimension extraction and graph building** when deterministic signals are incomplete or ambiguous.
- **G5 - Add deterministic and LLM-assisted cross links** so related pages can be traversed and retrieved as a memory graph.
- **G6 - Keep storage pluggable** by leaving transport and persistence in `IMemoryClient` implementations.
- **G7 - Preserve inspectability** by keeping page content human-readable markdown with explicit metadata.

### 3.2 Non-Goals

- Rebuilding the full scheduler maintenance job in this phase.
- Reproducing every source-repo maintenance heuristic on day one.
- Adding a new database or replacing GBrain.
- Building a UI for browsing memory pages in this phase.
- Solving all memory extraction quality problems. This PRD is about page structure and organization after extraction candidates exist.

### 3.3 In-Scope

Concrete deliverables for this phase, all under `src/Common/LeanKernel.Logic`:

- `MemoryPageParser`, `MemoryPageRenderer`, and `MemoryPageNormalizer` for deterministic 5W1H normalization.
- `MemoryDimensionClassifier` with deterministic scoring plus an optional small-model refinement pass.
- `MemoryPageLinker` (deterministic links) and `MemoryGraphReasoner` (bounded, validated small-model edges).
- `FactExtractionService` — ports `FactExtractionStep` logic to extract structured facts from conversation turns using a small-model LLM call; emits seed `# Learned Fact` pages with `Session`, `Turn`, `RecordedAt` metadata.
- Bounded, fill-missing-only LLM repair.
- A scope-relative page-key builder and markdown-embedded metadata, with **no `IMemoryClient` contract change** (see §16, D1/D2).
- A separately configured small-model client with token/concurrency budgets (§16, D3).
- Small-model prompt/response contracts (C# records + JSON serializer options) for dimension extraction and graph reasoning (§8.4, §9.3).
- Subject slug generation algorithm with collision resolution (§8.5).
- Error handling and degradation policies for small-model calls (timeouts, JSON parse failures, circuit breaker).
- Observability hooks (metrics, structured logging) for normalization pipeline.
- `MemoryProvider` writeback integration and unit/integration/golden tests.

### 3.4 Out of Scope

In addition to the Non-Goals in §3.2: no changes to the `IMemoryClient` / `MemoryItem` transport contract, no new GBrain tools, no scheduler or maintenance-job orchestration, no synchronous backlink write fan-out (backlinks stay lazy, §9.5), and no UI.

### 3.5 Ported vs. Net-New

Verified against source `JobExecutor.cs` and `FactExtractionStep.cs`:

- **Ported / adapted from source:** canonical 5W1H field set and order, `# Learned Fact` / `# Retired Fact` parse and render, missing-field detection, `complete` / `partial` status, bounded related-evidence collection (with concrete scores and caps), and fill-missing-only LLM repair with safe JSON extraction. **Fact extraction**: LLM prompt for fact extraction, conversation transcript builder, seed page renderer with `Session`/`Turn`/`RecordedAt`, JSON parsing with fallback. Records `FactPageSnapshot`, `RelatedEvidenceCandidate` / `RelatedEvidencePage`, and `PageNormalizationResult`.
- **Net-new design (no source equivalent):** dimension identification, scoring, and ranking (§8), primary-dimension key organization (§8.5), the small-model dimension pass (§8.4), the general cross-link graph beyond `supersedes` / `superseded-by` (§9), the small-model graph pass (§9.3), the `## Dimensions` and `## Links` rendered sections, and the small-model client/config plumbing.

The source has **no** dimension logic and **no** general graph logic (only a narrow `supersedes` / `superseded-by` pair). Treat those sections as design work, not extraction, and plan effort, risk, and test coverage accordingly.

### 3.6 Outcomes

Success for this phase means:

- A raw learned fact deterministically becomes a canonical 5W1H page with visible `## 5W1H`, `## Dimensions`, `## Links`, and `## Normalization` sections, with no fabricated fields.
- Every normalized page yields a `PrimaryDimension` plus ordered `SecondaryDimensions` and a stable, scope-relative page key.
- Deterministic cross links (with reasons and scores) are emitted; optional small-model dimension and graph passes refine results and always fall back deterministically.
- The full pipeline builds and runs with the small model disabled (NFR1 / NFR5), and all §11 acceptance criteria pass in `test/LeanKernel.Tests.Unit`.

---

## 4. Source Logic to Import

The source repo already contains the core behaviors we want.

### 4.1 Directly reusable concepts

From `JobExecutor.cs`:

- the canonical ordered field set: `Who`, `What`, `When`, `Where`, `Why`, `How`
- deterministic page rendering for `# Learned Fact` and `# Retired Fact`
- missing-field tracking via `Missing5W1H`
- normalization status via `complete` vs `partial`
- optional LLM repair that fills only missing fields
- related-evidence collection using explicit links, same-session evidence, and semantic similarity
- safe JSON extraction and constrained repair parsing

From `FactExtractionStep.cs`:

- the LLM prompt for fact extraction: *"Extract any new factual information from this conversation that should be remembered. Return only a JSON array of strings."*
- conversation transcript builder: user message + recent history (last 4 turns) + assistant response
- JSON array / bullet list / plain text parsing fallback
- the minimal learned fact page seed shape:

```markdown
# Learned Fact

<fact text>

- Session: <session>
- Turn: <turn>
- RecordedAt: <timestamp>
```

From `knowledge-fact-defrag-job.md`:

- 5W1H is a normalization contract, not just a display preference
- missing fields must remain visible instead of being silently fabricated
- related pages are valid evidence sources when bounded and explicit

### 4.2 Logic not imported directly

The following stays out of `LeanKernel.Logic` in this phase:

- retirement planning job loops,
- scheduled execution plumbing,
- job result persistence,
- page defrag batch orchestration,
- scheduler-owned prompts and counters.

---

## 5. Product Requirements

### 5.1 Functional Requirements

- **FR1** - `LeanKernel.Logic` must be able to normalize a raw or partially structured memory page into a canonical 5W1H page representation.
- **FR2** - The logic must preserve partial pages and explicitly list missing fields.
- **FR3** - The logic must identify the page's key dimensions and produce a ranked dimension list.
- **FR4** - The logic must support a small LLM reasoning pass that extracts dimensions, proposes dimension confidence, and suggests graph edges using structured output.
- **FR5** - The logic must generate deterministic cross links to related memory pages and allow the small-model reasoning pass to add bounded, evidence-backed links.
- **FR6** - The logic must render pages as human-readable markdown plus machine-parseable metadata lines.
- **FR7** - The logic must support both deterministic normalization and optional bounded LLM repair for missing fields only.
- **FR8** - `MemoryProvider` and `IMemoryClient` integrations must be able to store and retrieve the rendered page content and its derived metadata.
- **FR9** - Link generation must be explainable by storing link reasons such as `linked`, `same-session`, `same-turn`, `same-dimension`, `semantic`, `supersedes`, and `llm-inferred`.
- **FR10** - Derived metadata (fields, dimensions, links, provenance) must round-trip through the rendered markdown `content`, because `IMemoryClient` carries only `key` + `content` on write and only `Text` on read (see §16, D2). `MemoryPageParser` must re-derive this metadata on retrieval.
- **FR11** - The page-key builder must emit a scope-relative suffix (`facts/{primaryDimension}/{subjectSlug}/{factId}`) only; the persistence layer prepends the `memory/{tenant}/{user}/{channel}/` scope, so passing a fully-qualified path would double-prefix (see §16, D1).

### 5.2 Non-Functional Requirements

- **NFR1** - Deterministic normalization must work without an LLM. (Applies to `MemoryPageNormalizer`, `MemoryDimensionClassifier` deterministic path, `MemoryPageLinker`, `MemoryPageRenderer/Parser` — NOT to `FactExtractionService` which requires an LLM.)
- **NFR2** - The page format must remain readable in raw markdown.
- **NFR3** - The organization strategy must avoid duplicating the same page across many folders as the primary storage model.
- **NFR4** - Cross-link generation must be bounded to avoid unmanageable graph explosion.
- **NFR5** - The logic must be testable without a live GBrain service.
- **NFR6** - Small-model reasoning must be cheap, low-latency, and optional, with strict token and concurrency budgets.
- **NFR7** - The small model must be a separately configured client (model id, max output tokens, max concurrency, timeout, on/off), distinct from the primary chat client, so it can be disabled without affecting the deterministic path (see §16, D3).
- **NFR8** - Small-model calls must emit structured logs (pageKey, phase, durationMs, success, fallbackReason) and metrics (latency histogram, error counter, fallback counter) for observability.
- **NFR9** - Normalization pipeline must be instrumented with metrics: pages normalized (total/partial), dimension extraction source (deterministic/llm), link counts by relation type, LLM repair attempts/successes.

---

## 6. Target Design

### 6.1 Architectural boundary

`LeanKernel.Logic` should own:

- page parsing,
- 5W1H normalization,
- dimension identification,
- related-page scoring,
- cross-link generation,
- page rendering.

Gateway or other composition-root code should continue to own:

- HTTP transport,
- GBrain MCP transport,
- concrete `IMemoryClient` persistence,
- runtime wiring,
- optional batch job triggers.

### 6.2 Proposed service surface

Add a small service set under `src/Common/LeanKernel.Logic`:

- `MemoryPageNormalizer`
- `MemoryDimensionClassifier`
- `MemoryPageLinker`
- `MemoryGraphReasoner`
- `MemoryPageRenderer`
- `MemoryPageParser`
- `FactExtractionService`

Keep the public contract minimal. The implementation may use internal records similar to the source repo's `FactPageSnapshot`, `RelatedEvidencePage`, and `PageNormalizationResult`.

### 6.3 Proposed core models

Add logic-layer models similar to:

```csharp
public sealed record MemoryPageSnapshot(
    string Key,
    string Content,
    string FactText,
    string NormalizedFactText,
    DateTimeOffset EffectiveTimestamp,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, string?> Fields,
    string? SessionId,
    string? TurnId,
    IReadOnlyList<string> ExplicitLinks,
    string? SupersededBy);

public sealed record MemoryPageNormalizationResult(
    string Content,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<MemoryDimensionScore> Dimensions,
    IReadOnlyList<MemoryPageLink> Links)
{
    public bool IsPartial => MissingFields.Count > 0;
}

public sealed record MemoryDimensionScore(string Dimension, int Score, string Reason);

public sealed record MemoryPageLink(string TargetKey, string Relation, int Score, IReadOnlyList<string> Reasons);

// Small-model contracts (§8.4, §9.3)
public sealed record DimensionExtractionRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<RelatedEvidencePage> RelatedEvidence);

public sealed record DimensionExtractionResponse(
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyDictionary<string, string> DimensionRationales,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizedDimensionValues);

public sealed record GraphReasoningRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<MemoryPageLink> DeterministicLinks,
    IReadOnlyList<RelatedEvidencePage> CandidatePages);

public sealed record GraphReasoningResponse(
    IReadOnlyList<ProposedEdge> Links);

public sealed record ProposedEdge(
    string TargetKey,
    string Relation,
    double Confidence,
    IReadOnlyList<string> Reasons);
```

The final shape can vary, but these concepts are required.

### 6.4 Model tier configuration

Three distinct model tiers with separate configuration:

| Tier | Purpose | Example Models | Config Section |
|------|---------|----------------|----------------|
| **Primary** | Main chat/agent responses | gpt-4o, gpt-4.1 | `OpenAISettings.DefaultModel` |
| **Fact Extraction** | Conversation → fact strings (JSON array) | gpt-4o-mini, gpt-4o, gpt-4.1-mini | `FactExtractionSettings` |
| **Reasoning/Repair** | Dimensions, graph edges, field repair | gpt-4o-mini, phi-3-mini, haiku | `MemorySettings` |

**Fact extraction requires a more capable model** because:
- Input: full conversation transcript (up to 12K chars / ~3K tokens)
- Task: semantic identification of "new factual information" vs opinion/chatter
- Output: strict JSON array format compliance

The reasoning/repair passes operate on smaller, structured inputs with constrained outputs and can use cheaper models.

```csharp
public sealed class FactExtractionSettings
{
    public const string SectionName = "FactExtraction";
    public string ModelId { get; set; } = "gpt-4o-mini";  // distinct from OpenAISettings.DefaultModel when desired
    public double Temperature { get; set; } = 0.1;
    public int MaxOutputTokens { get; set; } = 1024;
    // No Enabled flag — extraction always requires LLM
}
```
- **Separate config for fact extraction**: `FactExtractionSettings` (model id, temperature, max tokens) registered as another keyed client (key: `"fact-extraction"`). This client has NO "enabled=false" fallback — extraction always requires an LLM.

---

## 7. Memory Page Format

### 7.1 Canonical page shape

Every normalized learned memory page should render in this shape:

```markdown
# Learned Fact

## 5W1H

- Who: ...
- What: ...
- When: ...
- Where: ...
- Why: ...
- How: ...

## Dimensions

- PrimaryDimension: what
- SecondaryDimensions: who, where

## Links

- Related: memory/.../who/jane-doe/... | same-session, semantic
- Related: memory/.../where/seattle/... | same-dimension

## Normalization

- NormalizationStatus: complete
- NormalizationMethod: deterministic
- Missing5W1H:

- Session: ...
- Turn: ...
- RecordedAt: ...
```

If fields are missing, they remain blank and `Missing5W1H` lists them. No field should be fabricated in deterministic mode.

### 7.2 Storage principle

Each memory page should be stored once as the canonical source page. Organization by dimension should happen through:

- the page key,
- page metadata,
- generated cross links,
- optional dimension index pages.

This avoids storing six duplicated copies of the same page.

---

## 8. Key Dimension Identification

This requirement is mandatory.

### 8.1 Purpose

Not every page is equally organized by every 5W1H field. A memory such as "Jane approved the Q4 budget in Seattle last week" has strong `Who`, `What`, `Where`, and `When`, but `What` may be the best primary anchor for retrieval while `Who` and `Where` remain useful secondary anchors.

The system must therefore identify the page's key dimensions and rank them.

### 8.2 Deterministic scoring model

For each of the six fields, compute a dimension score from simple deterministic signals.

Suggested signals:

- field present and non-empty,
- field length and specificity,
- whether the field contains a concrete named entity or timestamp,
- whether the field appears in the original fact text,
- whether the field is stable enough to act as an organizational anchor.

Suggested default weighting:

- `What`: base 100 when populated
- `Who`: base 80 when populated with a named actor
- `Where`: base 70 when populated with a concrete place/system/location
- `When`: base 60 when populated with a concrete or precise time
- `Why`: base 50 when populated with explicit rationale
- `How`: base 50 when populated with a concrete mechanism or process

Adjustments:

- add score for specificity,
- subtract score for vague values like `someone`, `recently`, `somewhere`, `for some reason`, `somehow`,
- add score when a field aligns with explicit page metadata,
- subtract score when a field is inferred only weakly.

### 8.3 Output contract

The classifier must output:

- `PrimaryDimension`
- ordered `SecondaryDimensions`
- per-dimension scores and reasons

This output must be included in normalization results so downstream storage and retrieval can use it.

### 8.4 Small-model reasoning for dimension extraction

Deterministic extraction should run first. A small LLM reasoning pass should run second when:

- multiple dimensions are plausible,
- deterministic signals are sparse,
- field values are present but weakly structured,
- graph-building would benefit from resolving implicit subjects, places, or actions.

This reasoning pass should use a small, low-cost model and return strict structured output only.

Suggested output shape:

```json
{
  "primaryDimension": "what",
  "secondaryDimensions": ["who", "where"],
  "dimensionRationales": {
    "what": "The page centers on an action or fact statement.",
    "who": "A specific actor is named.",
    "where": "A concrete location appears in the evidence."
  },
  "normalizedDimensionValues": {
    "who": ["Jane Doe"],
    "what": ["Q4 budget approval"],
    "where": ["Seattle office"]
  }
}
```

Rules:

- it may refine ranking but must not invent unsupported dimensions,
- it must be grounded only in page content and bounded related evidence,
- deterministic output remains the fallback when the LLM call fails or returns invalid output,
- the normalized page should retain whether dimensions came from deterministic logic, small-model reasoning, or both.

#### Error handling & degradation (dimension extraction)

- **Timeout**: respect `MemorySettings.TimeoutSeconds`; on timeout, log warning with `pageKey`, `attempt`, `elapsedMs` and fall back to deterministic dimensions.
- **Invalid JSON**: if response fails schema validation (missing required fields, extra fields, wrong types), log warning with raw response (truncated to 500 chars) and fall back.
- **Empty/low-confidence**: if `PrimaryDimension` is empty or confidence heuristics (e.g., rationale length < 10 chars) suggest low quality, treat as invalid and fall back.
- **Circuit breaker**: track consecutive failures per process; after 5 failures in 60s, disable small-model dimension pass for 5 min (configurable) and log metric.
- **Provenance**: every normalized page records `NormalizationMethod: deterministic | hybrid-llm` and per-dimension `Source: deterministic | llm-refined`.

### 8.5 Organization rule

The primary dimension should drive the canonical page key pattern.

Suggested key shape:

```text
memory/{tenantId}/{userId}/{channelId}/facts/{primaryDimension}/{subjectSlug}/{factId}
```

Where:

- `primaryDimension` is the top-ranked dimension in lowercase,
- `subjectSlug` is derived from the strongest field value for that dimension (see **Subject slug algorithm** below),
- `factId` is a stable unique suffix (e.g., ULID or SHA-256 prefix of fact text + timestamp).

Examples:

- `memory/<tenant>/<user>/<channel>/facts/what/q4-budget-approval/<id>`
- `memory/<tenant>/<user>/<channel>/facts/who/jane-doe/<id>`
- `memory/<tenant>/<user>/<channel>/facts/where/seattle-office/<id>`

If the top dimension is weak or tied, default to `what`.

#### Subject slug algorithm

1. Take the primary dimension's field value (e.g., `What: "Jane approved the Q4 budget in Seattle"`).
2. Extract the core entity/action phrase: prefer the first noun phrase or verb-object pair. Heuristic: split on stopwords, take longest meaningful token sequence (2-5 words).
3. Normalize: lowercase, replace non-alphanumeric with hyphens, collapse consecutive hyphens, trim hyphens, max 64 chars.
4. If result is empty or generic (`action`, `fact`, `event`), fall back to `fact-{factId}`.
5. Collision resolution: on `SaveMemoryAsync` conflict (slug exists with different factId), append `-{n}` where `n` increments from 2.

---

## 9. Cross Links and Page Graph

This requirement is mandatory.

### 9.1 Purpose

Memory pages should not behave like isolated notes. They should form a navigable graph so retrieval can move from a directly matched page to adjacent relevant pages.

### 9.2 Link types

Support these deterministic relation types:

- `supersedes`
- `superseded-by`
- `same-session`
- `same-turn`
- `same-dimension`
- `same-subject`
- `semantic-related`
- `explicit-related`

### 9.3 Link generation rules

When normalizing a page against a candidate set of related pages, generate links using these ordered signals:

1. explicit page metadata such as `Supersedes`, `SupersededBy`, or declared related keys,
2. same `SessionId`,
3. same `TurnId`,
4. overlap in top-ranked dimensions and normalized dimension values,
5. semantic similarity of normalized fact text,
6. shared named entities across `Who`, `Where`, or `What`.

After deterministic candidate collection, a small LLM reasoning pass may rerank or add a small number of edges when relationships are implicit but evidence-backed.

Examples:

- inferring that two pages share the same project or actor even when wording differs,
- linking an action page to a place page when the place is implied in the fact text,
- linking follow-up facts that continue the same event thread without exact string overlap.

Use bounded selection similar to the source repo's related-evidence collector:

- always keep explicit links,
- cap same-session links,
- cap semantic links,
- cap LLM-added links more aggressively than deterministic links,
- sort by score descending,
- deduplicate by target page key.

The small-model graph pass must emit structured edges only, for example:

```json
{
  "links": [
    {
      "targetKey": "memory/.../what/q4-budget-approval/abc123",
      "relation": "semantic-related",
      "confidence": 0.82,
      "reasons": ["shared-subject", "same-event-thread"]
    }
  ]
}
```

Every LLM-added edge must be retained only if it passes post-validation against known candidate pages and configured confidence thresholds.

#### Error handling & degradation (graph reasoning)

- **Timeout**: same `MemorySettings.TimeoutSeconds` budget; on timeout, drop LLM edges for this normalization, log warning, proceed with deterministic links only.
- **Invalid JSON / schema violation**: drop all proposed edges, log warning with truncation, proceed with deterministic links.
- **Confidence threshold**: configurable minimum (default 0.7); edges below threshold are dropped.
- **Candidate validation**: each proposed `TargetKey` must exist in the candidate set passed to the reasoner; unknown keys are dropped.
- **Cap enforcement**: LLM edges capped at `maxLlmEdgesPerPage` (default 3) regardless of model output count.
- **Provenance**: retained edges marked with `Relation: llm-inferred` and include `Confidence` in link metadata.

### 9.4 Link rendering

Every canonical page should include a `## Links` section in markdown and preserve the machine-readable form in metadata lines.

Example:

```markdown
## Links

- Related: memory/.../who/jane-doe/abc123 | same-subject, same-session
- Related: memory/.../where/seattle-office/def456 | same-dimension, semantic-related
- SupersededBy: memory/.../what/q4-budget-final/ghi789
```

### 9.5 Backlink strategy

Backlinks should not require rewriting every previously stored page synchronously.

Instead:

- forward links are written during normalization,
- backlinks are derived lazily during retrieval or refreshed by later maintenance,
- optional index pages can summarize inbound references for hot subjects.

This keeps write complexity low while still allowing graph traversal.

---

## 10. Adaptation Plan for LeanKernel.Logic

### 10.1 Phase 0 - Extract reusable fact extraction logic (LLM-required)

Port `FactExtractionStep` from `LeanKernel.Learning` into `LeanKernel.Logic` as `FactExtractionService`:

- LLM prompt for fact extraction (JSON array of strings) — **requires LLM call**
- Conversation transcript builder (user message + recent history + assistant response)
- Seed page renderer: `# Learned Fact` + fact text + `Session`/`Turn`/`RecordedAt` metadata
- Small-model client configuration (distinct from reasoning/repair models; uses extraction prompt)
- JSON parsing with fallback (array, bullet list, plain text)

**Note:** Fact extraction is fundamentally a semantic task and requires an LLM. There is no deterministic fallback. The "deterministic-first" guarantee (NFR1) applies to the normalization/linking/dimension pipeline that runs *after* facts are extracted.

### 10.2 Phase 1 - Extract reusable normalization logic

Implement deterministic logic in `LeanKernel.Logic` based on source `JobExecutor.cs`:

- canonical 5W1H field list,
- page parsing,
- page rendering,
- missing-field detection,
- normalization result object.

### 10.3 Phase 2 - Add dimension classification

Implement the dimension scoring and ranking mechanism in `MemoryDimensionClassifier`.

Deliverables:

- ranked dimensions,
- primary dimension selection,
- subject slug generation,
- page-key builder helper.

Add a small-model reasoning hook for ambiguous pages.

Deliverables:

- structured dimension extraction prompt,
- strict JSON response parsing,
- deterministic fallback when the model is unavailable,
- provenance markers for deterministic vs LLM-assisted dimensions.

### 10.4 Phase 3 - Add deterministic page linking

Adapt the source related-evidence approach into `MemoryPageLinker`.

Deliverables:

- candidate scoring,
- relation tagging,
- bounded link selection,
- markdown link rendering.

Add a graph-reasoning layer in `MemoryGraphReasoner`.

Deliverables:

- small-model edge proposal prompt,
- candidate-aware graph reasoning over bounded page sets,
- edge confidence thresholds,
- post-validation so unsupported edges are dropped.

### 10.5 Phase 4 - Optional bounded LLM repair

Add a narrow repair path that only fills missing 5W1H fields.

Constraints:

- deterministic mode remains the default,
- LLM repair cannot overwrite populated fields,
- related evidence is passed as untrusted context only,
- the result must be strict JSON with only the six expected keys.

### 10.6 Phase 5 - Integrate with existing memory flow

Update `MemoryProvider` and related writeback flow so saved memories can be normalized into canonical page content before persistence.

Likely changes:

- extend `IMemoryClient` or add a helper path for page-shaped writes,
- retain compatibility with raw-text retrieval during rollout,
- inject structured page content into prompt context when available.

---

## 11. Acceptance Criteria

- A raw learned fact can be converted into a canonical 5W1H page in `LeanKernel.Logic`.
- Partial pages remain partial and list exactly which 5W1H fields are missing.
- The system outputs a primary dimension and ordered secondary dimensions for every normalized page.
- Ambiguous pages can use a small-model reasoning pass to refine dimensions without breaking deterministic fallback.
- The canonical page key is organized by the chosen primary dimension.
- The system emits deterministic cross links with relation reasons and scores.
- The system can add bounded, validated LLM-assisted graph edges with provenance and confidence.
- The rendered markdown contains visible `## 5W1H`, `## Dimensions`, and `## Links` sections.
- The logic can run without an LLM.
- Optional LLM repair only fills missing fields and never silently overwrites existing fields.

---

## 12. Testing Strategy

### 12.1 Unit tests

- parser tests for learned pages and partial pages,
- normalization tests for complete vs partial output,
- dimension classification tests for actor-centric, place-centric, and action-centric facts,
- dimension classification tests covering ambiguous cases that require small-model refinement,
- link-generation tests for explicit links, same-session links, and semantic links,
- graph-building tests for validated small-model edge proposals,
- page-key generation tests for stable primary-dimension organization.

### 12.2 Integration tests

- `MemoryProvider` writes a page-shaped memory payload,
- retrieval can return normalized content,
- fallback still works when only raw text is available.

### 12.3 Golden-file tests

Use source-inspired markdown fixtures to verify exact rendered page shape.

---

## 13. Risks and Mitigations

- **R1 - Over-importing scheduler concerns**
  Mitigation: keep only normalization, scoring, and linking logic in `LeanKernel.Logic`.

- **R2 - Link graph explosion**
  Mitigation: cap links by relation type and total link count.

- **R3 - Weak dimension selection for vague facts**
  Mitigation: default weak or tied results to `what` and preserve scoring reasons for diagnostics.

- **R4 - Small-model reasoning invents unsupported dimensions or edges**
  Mitigation: require strict JSON, bounded evidence, post-validation, and deterministic fallback.

- **R5 - Prompt bloat from fully rendered pages**
  Mitigation: retrieval can inject compact page summaries while canonical page storage remains verbose.

- **R6 - LLM repair or graph reasoning drifting from source evidence**
  Mitigation: allow repair only for missing fields, use strict JSON parsing, and bound related evidence.

---

## 14. Implementation Notes

- The imported logic should preserve the source repo's ordered 5W1H field contract exactly.
- Use the source repo's deterministic formatting style as the baseline so pages remain comparable across repos. This applies to the `## 5W1H` and `## Normalization` blocks; the `## Dimensions` and `## Links` sections are additive and have no source equivalent (see §16, F7).
- Keep records and helper methods internal unless another project needs them.
- Prefer adapting the source algorithms rather than copying the entire scheduler class.
- Treat dimension identification and page linking as first-class outputs of normalization, not optional extras.
- Use small-model reasoning for dimension extraction and graph building, but keep deterministic extraction as the control path and fallback path.

---

## 15. Recommended Next Step

The authoritative, dependency-ordered, and verifiable task list is the **Implementation Checklist in §17**. As a summary, implement the logic in this order:

1. **Contracts, models, plumbing** (Phase 0)
2. **`FactExtractionService`** — port `FactExtractionStep` from source repo (**LLM-required**, Phase 1)
3. `MemoryPageParser` + `MemoryPageRenderer` (Phase 2)
4. `MemoryPageNormalizer` (Phase 3)
5. `MemoryDimensionClassifier` (Phase 4)
6. `MemoryPageLinker` (Phase 5)
7. `MemoryGraphReasoner` (Phase 5)
8. `MemoryProvider` writeback integration (Phase 7)
9. optional LLM repair hook (Phase 6)

Note: `MemoryPageLinker` precedes `MemoryGraphReasoner` because the deterministic linker produces the candidate set the reasoner reranks. Fact extraction (Phase 1) is the entry point that produces seed pages for the normalization pipeline. **Only fact extraction fundamentally requires an LLM**; the rest of the pipeline is deterministic-first with optional small-model refinement.

---

## 16. Architecture Review - Findings and Decisions

This section records the review of architectural soundness and implementation completeness against the current worktree (`src/Common/LeanKernel.Logic`) and the source repo. Items marked **Decision** are binding for implementation and are referenced by the checklist in §17.

### 16.1 Soundness summary

The core direction is sound. Extracting deterministic 5W1H normalization out of the scheduler-coupled `JobExecutor` into `LeanKernel.Logic` is a clean separation; the "store once, organize by key + metadata + links" principle (§7.2) avoids page duplication; and the deterministic-first / LLM-optional layering satisfies NFR1 and NFR5. The source logic verified as portable: the canonical field set `["Who","What","When","Where","Why","How"]` (`JobExecutor.cs:31`), page parse/render, missing-field plus `complete`/`partial` status, bounded related-evidence collection, and fill-missing-only LLM repair with safe JSON extraction.

### 16.2 Ported vs. net-new (scope-critical)

The PRD is titled "import 5W1H logic," but the source contains only about half of the mandated behavior. Normalization, parse/render, related-evidence, and LLM repair are **ports**; dimension identification/scoring (§8), primary-dimension key organization (§8.5), the general cross-link graph beyond `supersedes`/`superseded-by` (§9), both small-model reasoning passes (§8.4, §9.3), and the `## Dimensions` / `## Links` sections are **net-new design**. See §3.5. Plan effort, risk, and test coverage accordingly.

### 16.3 Findings and binding decisions

- **F1 / D1 - Page keys must be scope-relative to avoid double-prefixing.** `GBrainMemoryClient.BuildScopedSlug` already prepends `memory/{tenantId}/{userId}/{channelId}/` to every key (`GBrainMemoryClient.cs:75-78`). **Decision:** the Logic-layer key builder (§8.5) emits only the suffix `facts/{primaryDimension}/{subjectSlug}/{factId}`; passing the full `memory/...` path as `key` would double-prefix. The `memory/{tenant}/{user}/{channel}/...` values in §8.5 are the *effective stored* slug, not the argument to `SaveMemoryAsync`.

- **F2 / D2 - Derived metadata travels inside the rendered markdown; `IMemoryClient` is not changed.** `SaveMemoryAsync(scope, key, content)` accepts only a string, `MemoryItem` exposes only `{ Key, Text, Score, Source }` (`IMemoryClient.cs:63-77`), and GBrain persists via `put_page { slug, content }` (`GBrainMemoryClient.cs`). There is no structured-metadata channel. **Decision:** dimensions, links, fields, and provenance are serialized as machine-parseable lines inside `content`; `MemoryPageParser` re-derives them on read. This resolves the open "extend `IMemoryClient` or add a helper path" choice in §10.5 toward **no contract change**, preserving G6 (pluggable storage), NFR2 (raw-markdown readable), and the store-once principle (§7.2). Reflected in FR8/FR10.

- **F3 / D3 - The small model needs its own client and budget config.** Only one `IChatClient` is registered today, bound to `OpenAISettings.DefaultModel` (`IServiceCollectionExtensions.cs:29-42`). The reasoning and repair passes (FR4, §8.4, §9.3, NFR6/NFR7) require a distinct low-cost model. **Decision:** introduce a separately configured small-model `IChatClient` (keyed registration or a thin `IReasoningModel` wrapper) with config for model id, max output tokens, max concurrency, timeout, and an on/off switch. Deterministic normalization must fully function with it disabled or unavailable.

- **F4 / D4 - Writeback must supply Session, Turn, and timestamp.** `MemoryProvider.StoreAIContextAsync(InvokedContext)` persists only `AIContext.Key` + `AIContext.Text` (`MemoryProvider.cs:61-76`), but the seed page (per source `FactExtractionStep`) needs `Session`, `Turn`, and `RecordedAt`. **Decision:** define and wire the source of these values in the rebuild (e.g., `IPermit`, a turn/context accessor, or state-bag keys) before normalization runs; without them the seed cannot be faithfully produced and `When`/links degrade.

- **F5 / D5 - New services and the small-model client need DI registration.** `AddContextProviders` registers only `MemoryProvider` (`IServiceCollectionExtensions.cs:19-24`). **Decision:** register `MemoryPageParser`, `MemoryPageRenderer`, `MemoryPageNormalizer`, `MemoryDimensionClassifier`, `MemoryPageLinker`, `MemoryGraphReasoner`, and the small-model client, with lifetimes consistent with the scoped provider model.

- **F6 / D6 - Adopt the source's concrete deterministic defaults for parity.** Where behavior overlaps, reuse the source constants from the related-evidence collector: scores `linked +100`, `same-session +70`, `same-turn +20`, `semantic (>0.2) +round(similarity*30)`; caps `relatedPagesMax` / `sameSessionMax` / `semanticNeighborsMax`; order by score desc then key asc; dedup by target key; evidence snippet <= 320 chars. **Decision:** these are the deterministic defaults; net-new relation types (`same-dimension`, `same-subject`) and LLM edges layer on top with their own, tighter caps.

- **F7 - Page section order diverges from source; golden files cannot be byte-identical.** The source renders `## 5W1H` then `## Normalization` with metadata as trailing bullets; this PRD inserts `## Dimensions` and `## Links` between them (§7.1). **Decision:** keep the 5W1H and Normalization blocks source-comparable, treat Dimensions/Links as additive, and qualify the "comparable across repos" note in §14 accordingly.

- **F8 - Requirement defects fixed.** Duplicate `FR5`/`FR6` (two identical "render markdown + metadata" lines) collapsed to a single FR6; FR10, FR11, and NFR7 added to close the metadata-round-trip, key-suffix, and small-model-config gaps above.

### 16.4 Small-model configuration (D3, NFR7)

Use the existing `OpenAI:Memory` configuration branch (maps to `MemorySettings` in the current rebuild):

```json
{
  "OpenAI": {
    "Memory": {
      "ModelId": "gpt-4o-mini",
      "MaxOutputTokens": 512,
      "MaxConcurrency": 4,
      "TimeoutSeconds": 15,
      "Enabled": true
    }
  }
}
```

Registration pattern in `IServiceCollectionExtensions`:

```csharp
services.Configure<MemorySettings>(config.GetSection("OpenAI:Memory"));

services.AddChatClient(sp => {
    var settings = sp.GetRequiredService<IOptions<MemorySettings>>().Value;
    if (!settings.Enabled) return new DisabledChatClient();
    var client = new OpenAIClient(...).GetChatClient(settings.ModelId).AsIChatClient();
    return client;
}).ConfigureHttpClient(c => c.Timeout = settings.Timeout)
  .AddKeyedService<IChatClient>("small-model");

services.AddScoped<IReasoningModel>(sp => 
    new ReasoningModel(sp.GetRequiredKeyedService<IChatClient>("small-model")));
```

`DisabledChatClient` implements `IChatClient` and throws `NotSupportedException` on any call, ensuring fast failure when disabled.

### 16.5 Subject slug algorithm (D1, FR11, §8.5)

See §8.5 "Subject slug algorithm" for the deterministic extraction, normalization, and collision resolution steps. This is a pure function with no external dependencies, fully unit-testable.

### 16.6 Small-model JSON serializer options (§16.4, §8.4, §9.3)

Shared `JsonSerializerOptions` for all small-model structured I/O:

```csharp
new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Skip
}
```

Strictness: `AllowTrailingCommas = false`, `ReadCommentHandling = Skip` to reject non-standard JSON that some models emit.

### 16.4 Small-model client configuration (D3, NFR7, NFR8)

Use the current `MemorySettings` and `FactExtractionSettings` bindings already rooted under `OpenAI`:

```csharp
public sealed class MemorySettings
{
    public const string SectionName = "Memory";

    public string ModelId { get; set; } = "gpt-4o-mini";   // distinct from OpenAISettings.DefaultModel
    public int MaxOutputTokens { get; set; } = 512;
    public int MaxConcurrency { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 15;
    public bool Enabled { get; set; } = true;
}
```

Registration (keyed service, scoped to match `MemoryProvider` lifetime):

```csharp
services.AddKeyedChatClient("small-model", (sp, _) =>
{
    var cfg = sp.GetRequiredService<IOptions<MemorySettings>>().Value;
    if (!cfg.Enabled) return new DisabledChatClient(); // no-op implementation

    var client = new OpenAIClient(
        new ApiKeyCredential(cfg.ApiKey ?? sp.GetRequiredService<IOptions<OpenAISettings>>().Value.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl ?? sp.GetRequiredService<IOptions<OpenAISettings>>().Value.BaseUrl) });
    return client.GetChatClient(cfg.ModelId).AsIChatClient();
})
.UseFunctionInvocation()
.UseLogging();

// Thin wrapper for compile-time DI clarity
public interface IReasoningModel : IChatClient { }
services.AddKeyedScoped<IReasoningModel, ChatClientAdapter>("small-model");

// Fact extraction client (separate key, NO disabled fallback — always requires LLM)
services.Configure<FactExtractionSettings>(config.GetSection("OpenAI:FactExtraction"));

services.AddKeyedChatClient("fact-extraction", (sp, _) =>
{
    var cfg = sp.GetRequiredService<IOptions<FactExtractionSettings>>().Value;
    var client = new OpenAIClient(
        new ApiKeyCredential(cfg.ApiKey ?? sp.GetRequiredService<IOptions<OpenAISettings>>().Value.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl ?? sp.GetRequiredService<IOptions<OpenAISettings>>().Value.BaseUrl) });
    return client.GetChatClient(cfg.ModelId).AsIChatClient();
})
.UseFunctionInvocation()
.UseLogging();
```

Where `DisabledChatClient` throws `NotSupportedException` on `CompleteAsync` so callers can catch and fallback deterministically.

### 16.5 Subject slug generation algorithm (§8.5)

```
Input: primaryDimension (e.g., "what"), fieldValue (e.g., "Q4 budget approval")
Output: slug (e.g., "q4-budget-approval")

Algorithm:
1. Normalize: lowercase, trim
2. Replace non-alphanumeric runs with single hyphen: Regex.Replace(value, "[^a-z0-9]+", "-")
3. Trim leading/trailing hyphens
4. Truncate to 64 chars
5. If empty after normalization, use "unknown"
6. Collision resolution: if page key exists, append "-{factIdSuffix}" where factIdSuffix = first 8 chars of factId
```

This runs in the key builder helper; the `factId` is a ULID/UUID generated at normalization time.

### 16.6 Small-model prompt/response contracts (§8.4, §9.3)

**Dimension extraction request** (system prompt + user content):
```csharp
record DimensionExtractionRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<RelatedEvidencePage> RelatedEvidence);
```

**Dimension extraction response** (strict JSON, no prose):
```csharp
record DimensionExtractionResponse(
    string PrimaryDimension,                          // one of: who, what, when, where, why, how
    string[] SecondaryDimensions,                     // ordered, subset of remaining 5
    IReadOnlyDictionary<string, string> Rationales,   // per-dimension explanation
    IReadOnlyDictionary<string, string[]> NormalizedDimensionValues // extracted entities per dim
);
```

**Graph reasoning request:**
```csharp
record GraphReasoningRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    string PrimaryDimension,
    string[] SecondaryDimensions,
    IReadOnlyList<MemoryPageLink> DeterministicLinks, // candidate set with scores
    IReadOnlyList<RelatedEvidencePage> RelatedEvidence);
```

**Graph reasoning response:**
```csharp
record GraphReasoningResponse(
    GraphEdge[] Links);

record GraphEdge(
    string TargetKey,      // scope-relative key
    string Relation,       // one of: supersedes, superseded-by, same-session, same-turn, same-dimension, same-subject, semantic-related, explicit-related, llm-inferred
    double Confidence,     // 0.0-1.0
    string[] Reasons);     // evidence codes
```

JSON serializer options (shared):
```csharp
static readonly JsonSerializerOptions SmallModelJson = new(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Skip
};
```

### 16.7 Error handling & degradation policies

| Failure point | Detection | Fallback | Observability |
|---------------|-----------|----------|---------------|
| Small-model dimension call timeout | `TaskCanceledException` / `TimeoutException` | Use deterministic dimensions | Log warning + metric `small_model.dimension.timeout` |
| Small-model dimension invalid JSON | `JsonException` or schema validation fail | Use deterministic dimensions | Log warning + metric `small_model.dimension.parse_error` |
| Small-model dimension unsupported dimension returned | Response contains dimension not in 5W1H set | Drop unsupported, keep deterministic ranking | Log warning + metric `small_model.dimension.invalid_dim` |
| Small-model graph call timeout | `TaskCanceledException` | Skip LLM edges, keep deterministic links | Log warning + metric `small_model.graph.timeout` |
| Small-model graph invalid JSON / schema fail | `JsonException` / validation | Skip LLM edges | Log warning + metric `small_model.graph.parse_error` |
| Small-model graph edge fails post-validation | Target key not in candidate set OR confidence < threshold | Drop edge | Metric `small_model.graph.edge_dropped` |
| LLM repair timeout / parse fail / empty result | Any exception or empty `Dictionary` | Skip repair, page stays `partial` | Log warning + metric `small_model.repair.failed` |
| `MemoryPageNormalizer` throws in `StoreAIContextAsync` | Unhandled exception | Catch in `MemoryProvider`, log error, degrade to raw-text save | Structured log + metric `memory.normalization.error` |

All small-model calls use `CancellationToken` linked to `TimeoutSeconds` config. Concurrency limited via `SemaphoreSlim(MaxConcurrency)` in the wrapper.

---

## 17. Implementation Checklist

> Ordered by dependency. Each item is independently verifiable. `[ ]` = todo. All work lands under `src/Common/LeanKernel.Logic` unless noted. See §16 for the binding decisions (D1-D6) referenced below.

### Phase 0 - Contracts, models, and plumbing

- [x] Add core records: `MemoryPageSnapshot`, `MemoryPageNormalizationResult` (with computed `IsPartial`), `MemoryDimensionScore`, `MemoryPageLink`, and internal `RelatedEvidenceCandidate` / `RelatedEvidencePage` mirrors of the source shapes (§6.3).
- [x] Add the canonical field constant `["Who","What","When","Where","Why","How"]` as the single source of order (port of `JobExecutor.FiveWOneHFields`).
- [x] Add `MemorySettings` under `OpenAI:Memory` (`model id`, `max output tokens`, `max concurrency`, `timeout`, `enabled`) and keyed `IChatClient` / `IReasoningModel` registration (D3, NFR7, §16.4).
- [x] Add `FactExtractionSettings` config section (`model id`, `temperature`, `max output tokens`) and keyed `IChatClient` registration (key: `"fact-extraction"`) — NO enabled flag (§6.4).
- [x] Register all new services and both model clients via `AddContextProviders`/`AddMemoryPageServices` (D5).
- [x] Confirm and wire the writeback source of `Session`, `Turn`, and `RecordedAt` available to `MemoryProvider.StoreAIContextAsync` (D4).
- [x] Add subject slug generator helper with collision resolution (§16.5).
- [x] Add shared `JsonSerializerOptions` for small-model strict JSON (§16.6).
- [x] Add `DisabledChatClient` no-op implementation for when small model is disabled.

### Phase 1 - Fact extraction service (port from LeanKernel.Learning — **LLM-required**)

- [x] `FactExtractionService`: port `FactExtractionStep.ProcessAsync` — build transcript, call LLM with extraction prompt, parse JSON array / bullet list / plain text fallback.
- [x] `FactExtractionService`: render seed page per fact — `# Learned Fact` + fact text + `Session`/`Turn`/`RecordedAt` metadata lines.
- [x] Extraction model config is separate from reasoning/repair (`FactExtractionSettings`).
- [x] Unit tests: transcript -> facts -> seed page and malformed output fallback.
- [x] **No deterministic fallback** — extraction requires LLM; deterministic guarantee applies downstream.

### Phase 2 - Parser + Renderer (port)

- [x] `MemoryPageParser`: parses learned/retired headings, fact text, metadata, dimensions, and links.
- [x] `MemoryPageParser`: re-derives `## 5W1H`, `## Dimensions`, and `## Links` metadata from markdown content (D2, FR10).
- [x] `MemoryPageRenderer`: renders canonical page sections and normalization metadata in stable order (§7.1).
- [ ] Golden-file fixture/approval infrastructure (parser/renderer roundtrip coverage exists, but golden-file harness is not yet implemented).

### Phase 3 - Deterministic normalizer (port)

- [x] `MemoryPageNormalizer`: maps fields, computes `MissingFields`, and sets `complete` / `partial` with deterministic default.
- [x] Deterministic path does not fabricate fields; missing values remain explicit (FR2).
- [x] Unit tests cover complete/partial and no-LLM flow (NFR1).

### Phase 4 - Dimension classification (net-new)

- [x] `MemoryDimensionClassifier`: deterministic scoring, ranked dimensions, primary/secondary output, and reasons.
- [x] Subject-slug generator + scope-relative key builder emitting `facts/{primaryDimension}/{subjectSlug}/{factId}` only (D1, FR11).
- [x] Optional small-model dimension refinement with strict JSON parse + deterministic fallback + provenance source.
- [ ] Token-budget-aware prompt truncation is not yet implemented.
- [ ] Full §8.4 degradation set (notably circuit breaker) is not yet implemented.
- [ ] Observability is partial (basic counters/histograms exist; full matrix in §8.4/§16.7 not complete).
- [x] Unit tests cover actor/place/action style cases, ambiguous refinement/fallback, and key stability.

### Phase 5 - Linking + graph (port + net-new)

- [x] `MemoryPageLinker`: deterministic scoring/selection with relation reasons, sort order, and dedup.
- [x] `MemoryGraphReasoner`: optional model edges with post-validation (candidate membership + confidence threshold) and capped LLM additions.
- [ ] Graph prompt token-budget enforcement is not yet implemented.
- [x] Core §9.3 degradation paths implemented (invalid JSON/timeout -> deterministic-only; threshold + candidate validation + caps).
- [x] Backlinks remain lazy (no synchronous backlink fan-out on write).
- [ ] Observability is partial (link count and graph duration exist; full validation-rate metrics not yet complete).
- [x] Unit tests cover deterministic and LLM-edge validation, rejection, dedup, and cap enforcement.

### Phase 6 - Bounded LLM repair (port)

- [x] Repair path fills only missing fields via strict JSON object extraction; populated fields are never overwritten.
- [x] Related evidence is passed as context only; deterministic path remains fallback when unavailable/invalid.
- [x] Unit tests cover missing-only fill, invalid JSON fallback, and populated-field protection.

### Phase 7 - MemoryProvider writeback integration

- [x] `MemoryProvider.StoreAIContextAsync`: build seed page from text + Session/Turn/RecordedAt, normalize to canonical content, and persist with scope-relative key (D1, D2).
  - **Session/Turn/RecordedAt wiring**: extend `InvokedContext` or introduce `ITurnContextAccessor` (scoped) resolved in `MemoryProvider` to provide `TurnId`, `SessionId` (from `IPermit.SessionId` or conversation state), and `RecordedAt = TimeProvider.GetUtcNow()`. If unavailable, omit from seed page (degrades `When` field and same-session/same-turn links).
- [x] `MemoryProvider.ProvideAIContextAsync`: retrieval remains raw-text compatible and injects compact summaries to bound prompt size (R5).
  - **Compact summary**: parse retrieved page with `MemoryPageParser`, render summary as `- {PrimaryDimension}: {FactText} [dimensions: {dims}] [links: {count}]` (max 200 chars per page).
- [ ] Integration-test project coverage for write/read/fallback remains pending (currently covered by unit tests).

### Phase 8 - Acceptance verification

- [ ] Full §11 acceptance criteria demonstrated end-to-end.
- [x] `dotnet build` and `dotnet test test/LeanKernel.Tests.Unit` are green.
- [ ] Golden fixtures are not yet committed.
