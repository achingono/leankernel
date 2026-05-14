# PRD: LLM Wiki Extraction and Indexed Wiki Store

## Overview

Replace deterministic wiki fact extraction with an LLM-backed extraction pipeline that produces structured 5W1H facts, then refactor `WikiStore` into a human-readable, file-backed database with durable indexes that the existing `config/indexer/indexer.py` Qdrant sidecar consumes for fact-level semantic retrieval.

This PRD is an engineering planning document for developers implementing the wiki extraction, storage, migration, and retrieval changes.

> **This PRD has been revised.** A consolidated diff against the original draft, with validated reasons for each material change and citations to the codebase, is documented in the [Revisions Appendix](#revisions-appendix) at the end of this file.

## Problem Statement

The current wiki has low signal and inconsistent storage. Most generated files are under `data/wiki/llm`, while canonical 5W1H folders are mostly empty. LLM-generated IDs use `llm-...` prefixes, and `WikiStore.ResolvePath` interprets the first ID segment (split on `-`) as a directory, so entries can be physically stored outside their declared dimension and any subject whose slug starts with a dimension token (`what-if-analysis`, `where-clause`, `how-to-guide`) is silently mis-resolved.

The deterministic `WikiExtractor` is still active in the learning pipeline, conversation compaction, and chat scrub job. It produces shallow regex facts that are often less useful than the surrounding conversation. The existing LLM extractor is better, but it returns a flat array with one dimension per item and does not preserve all six 5W1H dimensions for each discovered fact.

`WikiQueryTool.ExecuteAsync` currently passes the entire raw `parametersJson` envelope as `WikiQuery.TextQuery`, so wiki search via tools effectively does not work today. The store also behaves like a folder of markdown files rather than a small database. Queries scan files, dedupe is exact-string based, metadata is hidden in inline HTML comments, and there is no durable index for fast lookup, sidecar-driven Qdrant sync, migration, aliases, or LLM-friendly navigation.

## Goals

- Replace deterministic fact extraction with one LLM-backed extraction contract.
- Preserve all six 5W1H dimensions for every discovered fact.
- Keep wiki records easy for humans to read as markdown.
- Make wiki records easy for an LLM to navigate during analysis.
- Make wiki facts indexable by Qdrant at fact-level granularity, owned by the existing Python sidecar.
- Refactor `WikiStore` into an indexed, file-backed store with stable IDs, atomic and serialised writes.
- Migrate existing `data/wiki/llm` entries into canonical 5W1H folders with cross-dimension collision quarantine.
- Improve signal-to-noise through input-side gating, quality gates, dedupe, pruning, and ranked retrieval.
- Fix `WikiQueryTool` parameter parsing so tool-driven search actually works.

## Non-Goals (v1)

- Replace markdown storage with a relational database.
- Depend on Qdrant as the source of truth.
- Build a full admin curation workflow for manual fact review.
- Implement cross-language semantic normalisation beyond deterministic English-first slug normalisation.
- Automatically delete high-confidence facts without migration logs or quality safeguards.
- Add a second Qdrant writer in C#. Wiki vector indexing remains owned by the existing Python sidecar.

## User Stories

- As a developer, I can inspect wiki markdown files and understand facts, sources, aliases, and relationships without reading hidden metadata.
- As the LLM, I can discover useful facts by scanning summaries, aliases, dimension folders, and related links.
- As the retrieval layer, I can search Qdrant at fact-level precision instead of embedding whole wiki entries.
- As an operator, I can rebuild indexes and Qdrant projections from markdown if generated state is lost.
- As the system, I can reject vague process noise and prefer specific, grounded, high-confidence facts.

## Functional Requirements

### FR-1 LLM extraction contract

`LlmWikiExtractor` must request and parse a single JSON object with a `facts` array, using LiteLLM/OpenAI strict JSON mode (`response_format = { "type": "json_object" }`). Each fact must include:

- `who`
- `what`
- `when`
- `where`
- `why`
- `how`
- `claim`
- `subject`
- `primaryDimension`
- `sourceQuote`
- `summaryHint`
- `aliases`
- `tags`

The parser must reject malformed JSON, invalid dimensions, blank claims, generic subjects, and ungrounded facts that lack a useful claim or source quote. At least one of the six 5W1H fields must be populated. Invalid responses must be logged with source ID and failure reason rather than silently swallowed.

The extractor MUST NOT request or trust an LLM-emitted confidence score. Confidence is computed deterministically by the mapper (see FR-4 / FR-8).

### FR-2 Extraction interface

Runtime callers must depend on an extraction interface, `IWikiFactExtractor`, that returns parsed but unmapped DTOs:

```csharp
Task<IReadOnlyList<ExtractedWikiFact>> ExtractAsync(
    string userMessage,
    string assistantResponse,
    string sourceId,
    CancellationToken ct);
```

A separate `WikiFactMapper` (FR-4) converts `ExtractedWikiFact` DTOs into `WikiEntry`/`WikiFact` records. Keeping these concerns separate prevents the Step 2 ↔ Step 4 contradiction in the original draft and lets the store own canonicalisation.

Callers to update:

- `LlmFactExtractionStep`
- `ConversationCompactor`
- `ChatFactScrubJob`

The deterministic extractor must remain only until the LLM interface, parser, mapper, and indexed store path are stable and tested.

### FR-3 Canonical markdown records

The wiki must store one canonical markdown file per subject and primary dimension:

```text
data/wiki/{dimension}/{subject-slug}.md
```

Canonical files must not include extraction dates or `llm-` prefixes. Dates and source IDs belong in fact metadata, not entry IDs or filenames.

Slug-collision policy: when two distinct subjects normalise to the same slug, append a deterministic disambiguation suffix (`-2`, `-3`, ...) and record the original subject string as an alias.

Each markdown file must be readable in a terminal and render clearly in GitHub, and must round-trip losslessly through `WikiStore.SerializeToMarkdown` / `ParseMarkdown`. Long-term storage replaces inline HTML comment metadata with:

- YAML frontmatter (`id`, `dimension`, `subject`, `summary`, `aliases`, `tags`, `lastAccessed`, `accessCount`)
- `# {Subject}`
- `## Summary` — human prose
- `## Facts` — human-readable bulleted claims
- A single fenced ` ```yaml lk-facts ` block holding the canonical structured per-fact records (5W1H, source quote, normalised key, confidence, source, last confirmed, tags). This is the source of truth for `WikiFact` round-trip and for sidecar parsing.
- `## Also Known As` — alias bullets
- `## Related` — markdown links

Markdown tables (with `|` separators) are explicitly NOT used for canonical structured data because GFM tables do not safely escape `|`, newlines, or backticks that occur naturally in claims and source quotes. Tables remain acceptable for purely human read-only views (UI), not for storage.

### FR-4 File-backed index

`WikiStore` must maintain generated metadata in `data/wiki/.LeanKernel/`:

- `index.json` — primary index
- `migration.json` — append-only migration ledger
- `migration.completed` — sentinel marking one-shot migration done
- `mutations.ndjson` — append-only mutation log used to rebuild a stale index incrementally

`index.json` must include enough metadata for fast filtering and ranked lookup without parsing every markdown body on every query. It must also support cross-dimensional retrieval: every populated 5W1H field on a fact contributes a pointer in the corresponding dimension's `factPointers` list, even though the fact is only stored once on disk under its primary dimension.

Concurrency policy: all mutations to `index.json` and to wiki markdown go through `WikiStore` and are serialised by an in-process `SemaphoreSlim`. Each write re-reads the latest persisted index before merging and re-serialising. Writes are atomic via temp-file + rename.

Index rebuild policy:

- On startup, load `index.json`. If missing, version-mismatched, or schema-mismatched, rebuild synchronously from markdown before serving any wiki query.
- During steady state, mark the index dirty on each write and persist incrementally; a periodic compaction pass re-derives `index.json` from markdown.

### FR-5 Database-like store behaviour

`WikiStore` must support:

- dimension-safe path resolution that derives the dimension from `WikiEntry.Dimension` (writes) or `index.json` (reads), never by string-splitting the entry ID
- stable canonical IDs of the form `{dimension}-{subject-slug}` resolved through the index, with the index treated as authoritative for dimension assignment
- atomic markdown writes
- atomic and serialised index writes (FR-4)
- startup-or-on-demand index rebuild (FR-4)
- index invalidation when markdown changes
- indexed `GetAsync`
- indexed `ListByDimensionAsync` (uses cross-dimensional `factPointers`, not just folder scan)
- indexed and ranked `QueryAsync`
- merge by normalised fact key
- migration from old `llm` storage (FR-7)

Unknown top-level dimensions such as `llm` must not be used for new writes.

### FR-6 Qdrant fact indexing (sidecar-owned)

Wiki vector indexing into the unified `LEANKERNEL_knowledge` Qdrant collection is owned by the existing Python sidecar `config/indexer/indexer.py`, which already watches `data/wiki/`, parses markdown frontmatter, embeds, and upserts. No C# component writes to Qdrant.

The PRD changes the sidecar contract as follows:

- The sidecar parses the new ` ```yaml lk-facts ` block in addition to frontmatter.
- For each `WikiFact`, the sidecar emits one Qdrant point for the normalised claim and one optional point for the source quote when present.
- Point IDs are `uuid5(NAMESPACE_LK_WIKI, factKey + ':' + vectorType)` so they are stable, deterministic, and satisfy Qdrant's UUID/uint64 ID constraint.
- Payload fields:
  - `entryId`
  - `factKey`
  - `vectorType` (`claim` | `quote`)
  - `dimension`
  - `subject`
  - `subjectSlug`
  - `claim`
  - `sourceQuote`
  - `confidence`
  - `lastConfirmed`
  - `source`
  - `tags`
  - `source_type` set to `"wiki"` to remain compatible with `KnowledgeSearchService` filters
- The sidecar maintains its own state DB (already present) so wiki re-indexing is idempotent and resumable. The C# side does not maintain a parallel `qdrant-sync.json`.

The markdown wiki and `index.json` must be sufficient to rebuild the Qdrant collection by re-running the sidecar.

### FR-7 Migration

A one-shot migration command must:

1. Detect old `data/wiki/llm` files.
2. Parse old markdown using backward-compatible parser logic.
3. Infer canonical dimension from old frontmatter, not directory.
4. Drop date suffixes and `llm-` prefixes from canonical IDs.
5. Use `(inferredDimension, normalizedSubject)` as the merge key. Cross-dimension subject collisions (e.g. the same slug `assistant` appearing across `who` and `what`) are NOT auto-merged; they go to `data/wiki/.LeanKernel/quarantine/{originalRelativePath}` with a `reason` field appended to `migration.json`.
6. Quarantine other low-signal files with explicit reasons.
7. Record source path, target entry ID, migrated fact keys, and timestamp in `migration.json`.
8. Rebuild `index.json` after migration.
9. Write `data/wiki/.LeanKernel/migration.completed` with the run timestamp.

The migration runs as an explicit one-shot operation (CLI/admin task), NOT as part of the periodic `WikiCompiler.CompileAsync` loop. `WikiCompiler` short-circuits the migration step when `migration.completed` exists. Re-running migration must be safe (idempotent) but the periodic maintenance loop must not probe `data/wiki/llm` indefinitely.

### FR-8 Quality gates

The store, mapper, and extractor must reject or quarantine low-signal facts, applying gates on both sides of the LLM call:

**Input-side gates** (cheaper — applied before calling the LLM):

- Skip extraction when the user message contains no first-person, declarative, or directive content above a minimum length threshold.
- Skip extraction when the assistant turn is pure scaffolding (only enumerations of file paths, tool outputs, or boilerplate greetings) with no quoted user statement.
- These gates address the empirically dominant source of `data/wiki/llm` noise.

**Output-side gates** (applied to extractor results):

- `Unknown` subjects
- Generic subjects such as `action`, `information`, `document`, or `file`
- Claims such as `Files updated successfully`
- Claims without user-relevant 5W1H context
- Duplicate or near-duplicate facts (by `NormalizedKey`)
- Facts below configured confidence or specificity thresholds

**Confidence assignment** (deterministic, no LLM self-rating):

The mapper computes confidence from observable signals:

- Base score by source type: user-stated > assistant-cited > assistant-claimed > assistant-inferred.
- +bonus when `sourceQuote` is non-empty and verbatim from the input exchange.
- +bonus on agreement-across-turns (later confirmation of a previously-extracted fact, found via `NormalizedKey`).
- −penalty when the claim is shorter than a configured token floor.

Final confidence is clamped to `[0, 1]`. The score schedule is config-driven so it can be tuned without code changes.

### FR-9 Consumer updates

Consumers must use indexed and ranked retrieval:

- `WikiQueryTool` — must parse its `parametersJson` envelope into `{ query, dimensions[], maxResults }` instead of passing the raw JSON string as the text query (this is a current bug, not a future enhancement). Tool-level errors when parameters are malformed must return a clear `ToolResult.Error` rather than an empty result set.
- `ContextCandidateRetriever` — uses indexed/ranked queries, prefers high-confidence high-specificity facts, returns compact summaries.
- `WikiController` — uses indexed counts and queries.
- `Wiki.razor` — shows summaries, aliases, 5W1H context, source quotes, related links. UI/admin detail views may render 5W1H as visible markdown tables; these are display-only and not parsed back.

Tool and prompt outputs should include fewer, higher-quality facts. UI/admin detail views may show richer provenance.

## Non-Functional Requirements

- Markdown remains the canonical source of truth.
- Generated indexes and Qdrant state are rebuildable projections.
- File writes must be atomic enough to avoid partial records after process interruption (temp-file + rename).
- Index mutations are serialised in-process (`SemaphoreSlim`) and re-read latest before merging to prevent lost updates from concurrent jobs.
- Query behaviour must avoid scanning every markdown file when the index is valid.
- Migration must be safe to run more than once.
- LLM extraction failures must not advance scrub checkpoints.
- Interactive request paths must never block on LLM extraction; extraction runs in `ChatFactScrubJob` and `ConversationCompactor` (or, when invoked from the learning pipeline, on a background task as `LlmWikiExtractor.ExtractAsync` already supports).
- New query and ingest paths must preserve existing public behaviour where possible.

## Architecture

| Component | Responsibility |
| --------- | -------------- |
| `IWikiFactExtractor` | Interface for extracting structured 5W1H facts from turns; returns DTOs. |
| `LlmWikiExtractor` | Calls LiteLLM in strict JSON mode, parses root JSON object, validates DTOs, does not assign confidence. |
| `WikiFactMapper` | Converts `ExtractedWikiFact` DTOs into canonical `WikiEntry` and `WikiFact` records, computes deterministic confidence, normalises subjects, generates IDs and `NormalizedKey`. |
| `WikiStore` | File-backed indexed store over markdown records. Owns serialised index mutations. |
| `WikiIndex` | In-memory and persisted projection of wiki entry metadata, including per-dimension `factPointers`. |
| `WikiCompiler` | Periodic maintenance: prune, dedupe, recompute confidence, rebuild index. Skips migration when `migration.completed` exists. |
| `wiki-migrate` (one-shot) | CLI/admin task that runs the FR-7 migration once and writes the sentinel. |
| `config/indexer/indexer.py` (existing sidecar) | Owns Qdrant writes for the wiki. Extended to parse `lk-facts` block and emit per-fact points. |
| `WikiQueryTool` | Tool-facing search; parses `parametersJson` properly; uses ranked indexed queries. |
| `ContextCandidateRetriever` | Prompt context retrieval using indexed wiki candidates. |

## Data Model

### Extraction response

```json
{
  "facts": [
    {
      "who": "Alfero Chingono",
      "what": "Communication preference",
      "when": "",
      "where": "",
      "why": "Reduce response noise",
      "how": "Use concise, direct answers",
      "claim": "Alfero Chingono prefers concise, direct assistance.",
      "subject": "Alfero Chingono",
      "primaryDimension": "who",
      "sourceQuote": "I prefer concise responses.",
      "summaryHint": "Alfero prefers concise and direct assistant responses.",
      "aliases": ["Alfero"],
      "tags": ["communication", "preference"]
    }
  ]
}
```

Note: there is intentionally no `confidence` field — confidence is computed by `WikiFactMapper` (FR-8).

### Wiki entry additions

Add these fields to `WikiEntry`:

```csharp
public string? Summary { get; init; }
public List<string> Aliases { get; init; } = [];
public List<string> Tags { get; init; } = [];
```

### Wiki fact additions

Add structured context and provenance fields:

```csharp
public WikiFactContext? Context { get; init; }
public string? SourceQuote { get; init; }
public string? NormalizedKey { get; init; }
public List<string> Tags { get; init; } = [];
```

### Wiki fact context

```csharp
public sealed record WikiFactContext
{
    public string? Who { get; init; }
    public string? What { get; init; }
    public string? When { get; init; }
    public string? Where { get; init; }
    public string? Why { get; init; }
    public string? How { get; init; }
}
```

### Index schema

`data/wiki/.LeanKernel/index.json`:

```json
{
  "version": 2,
  "builtAt": "2026-05-10T22:45:37.5808861+00:00",
  "entries": [
    {
      "id": "who-alfero-chingono",
      "dimension": "who",
      "subject": "Alfero Chingono",
      "normalizedSubject": "alfero chingono",
      "summary": "Alfero Chingono is the user...",
      "aliases": ["Alfero", "Chingono Alfero"],
      "tags": ["who", "alfero-chingono", "high-confidence"],
      "filePath": "who/alfero-chingono.md",
      "factCount": 4,
      "maxConfidence": 0.92,
      "lastConfirmed": "2026-05-10T00:00:00+00:00",
      "sources": ["conversation:2026-05-10T23:23:19"],
      "factKeys": [
        "who-alfero-chingono|prefer-concise-direct-assistance"
      ]
    }
  ],
  "factPointers": {
    "who":   [{ "factKey": "who-alfero-chingono|prefer-concise-direct-assistance", "entryId": "who-alfero-chingono" }],
    "what":  [{ "factKey": "who-alfero-chingono|prefer-concise-direct-assistance", "entryId": "who-alfero-chingono" }],
    "why":   [{ "factKey": "who-alfero-chingono|prefer-concise-direct-assistance", "entryId": "who-alfero-chingono" }],
    "how":   [{ "factKey": "who-alfero-chingono|prefer-concise-direct-assistance", "entryId": "who-alfero-chingono" }],
    "when":  [],
    "where": []
  }
}
```

`factPointers[dimension]` lets `ListByDimensionAsync(WikiDimension.Why)` return facts whose primary dimension is something else (e.g. `who`) but whose `Why` field is populated, without scanning every folder.

### Markdown record format

```markdown
---
id: who-alfero-chingono
dimension: who
subject: Alfero Chingono
summary: Alfero Chingono is the user and has professional, technical, and personal context captured by the wiki.
lastAccessed: 2026-05-10T22:45:37.5808861+00:00
accessCount: 0
aliases:
  - Alfero
  - Chingono Alfero
tags:
  - who
  - alfero-chingono
---

# Alfero Chingono

## Summary

Alfero Chingono prefers concise, direct assistance and has technical context that can guide future responses.

## Facts

- Alfero Chingono prefers concise, direct assistance.

```yaml lk-facts
- claim: Alfero Chingono prefers concise, direct assistance.
  normalizedKey: who-alfero-chingono|prefer-concise-direct-assistance
  context:
    who: Alfero Chingono
    what: Communication preference
    why: Reduce response noise
    how: Use concise answers
  sourceQuote: I prefer concise responses.
  source: conversation:2026-05-10T23:23:19
  confidence: 0.92
  lastConfirmed: 2026-05-10
  tags: [communication, preference]
```

## Also Known As

- Alfero
- Chingono Alfero

## Related

- [what: Communication preferences](../what/communication-preferences.md)
```

The fenced `lk-facts` block is the canonical structured store. `## Facts` is a human-prose mirror regenerated from it on every write.

## API Requirements

| Method | Endpoint | Requirement |
| ------ | -------- | ----------- |
| GET | `/api/wiki/dimensions` | Use indexed counts and fact totals |
| GET | `/api/wiki/entries` | Use ranked indexed query results |
| GET | `/api/wiki/entries/{entryId}` | Resolve via canonical index and return full record |

## UI Requirements

The wiki page should remain a human browsing tool and add fields that help evaluate quality:

- summary preview on entry cards
- aliases in entry detail
- 5W1H context table (display-only — never parsed back)
- source quote and confidence table (display-only)
- dimension-qualified related links
- high-signal facts before low-signal facts

## Security and Privacy

- Do not write secrets into wiki facts.
- Source quotes should be short and directly relevant.
- Qdrant payloads must not include hidden system prompts or credentials.
- Migration should quarantine questionable records instead of deleting them silently.
- Logs for malformed LLM extraction must avoid dumping sensitive full conversations.

## Telemetry and Success Metrics

- `wiki_extraction_llm_success_total`
- `wiki_extraction_llm_rejected_total`
- `wiki_extraction_parse_failure_total`
- `wiki_facts_ingested_total`
- `wiki_facts_quarantined_total`
- `wiki_index_rebuild_total`
- `wiki_index_rebuild_drift_total` (raised when the periodic rebuild produces a different `index.json` than was on disk — indicator of concurrency or schema drift)
- `wiki_qdrant_points_synced_total` (sidecar metric)
- Reduction in `data/wiki/llm` files to zero after migration
- Increase in query precision for wiki-backed context

## Rollout Plan

1. Phase 0: add DTOs, parser, mapper, and extraction interface; enable strict JSON mode.
2. Phase 1: add indexed `WikiStore` behaviour (single-writer serialisation, `factPointers`) while keeping current markdown compatibility.
3. Phase 2: add visible markdown sections + `lk-facts` block and index rebuild. Round-trip tests for both old and new formats.
4. Phase 3: extend `config/indexer/indexer.py` to parse `lk-facts` and emit per-fact Qdrant points with UUID5 IDs.
5. Phase 4: run idempotent `wiki-migrate` one-shot from `data/wiki/llm`; write `migration.completed` sentinel.
6. Phase 5: remove deterministic runtime extraction.
7. Phase 6: fix `WikiQueryTool` parameter parsing; update UI/API/tool consumers and tune ranking/quality thresholds.

## Acceptance Criteria

- AC-1: Runtime fact extraction no longer uses deterministic regex logic.
- AC-2: LLM extraction parses a root JSON object with all six 5W1H fields per fact, using strict JSON mode.
- AC-3: New wiki files are written under canonical dimension folders with stable IDs derived from `WikiEntry.Dimension` (not from string-splitting the ID).
- AC-4: Markdown files are readable; canonical structured per-fact data lives in a fenced `lk-facts` YAML block; no GFM tables are used for round-trip data.
- AC-5: `data/wiki/.LeanKernel/index.json` can be deleted and rebuilt from markdown alone, and the rebuilt index is structurally equal (modulo `builtAt`) to the previous one — verified by an automated test.
- AC-6: Qdrant points are generated by the Python sidecar (one per claim, optional one per source quote) with UUID5 IDs; no C# code writes to Qdrant.
- AC-7: `wiki-migrate` moves valid `data/wiki/llm` records into canonical folders, writes `migration.completed`, and is idempotent. `WikiCompiler.CompileAsync` is a no-op for migration when the sentinel exists.
- AC-8: Low-signal generic entries are rejected or quarantined. Cross-dimension subject collisions during migration go to quarantine, not auto-merge.
- AC-9: `WikiQueryTool` parses its `parametersJson` envelope into `{query, dimensions[], maxResults}` and the tool returns ranked results for a normal query.
- AC-10: Confidence on a `WikiFact` is computed by `WikiFactMapper`, never set from an LLM-emitted field.
- AC-11: Concurrent ingestion from `ChatFactScrubJob` and `ConversationCompactor` does not lose entries — verified by a test that runs both against an in-memory store and asserts the union of facts is preserved.
- AC-12: Existing build, unit tests, and coverage gate pass after implementation.

## Dependencies

- LiteLLM chat completions configuration with strict JSON mode support.
- Existing `IWikiStore`, `WikiEntry`, and `WikiFact` models.
- Existing Python sidecar (`config/indexer/indexer.py`) and embedding/Qdrant infrastructure.
- `WikiCompiler` scheduled maintenance.
- Current wiki UI, controller, and query tool consumers.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| LLM emits malformed JSON | Strict JSON mode, typed parser, strict validation, logged rejection, no checkpoint advancement on failure |
| Migration corrupts useful data | Idempotent migration log, quarantine path, canonical merge tests, cross-dimension collision guard |
| Markdown parser becomes brittle | Versioned markdown format, fenced `lk-facts` YAML for round-trip, round-trip tests for old and new formats |
| Sidecar and markdown drift | Markdown/index is source of truth; sidecar is rebuildable; `wiki_qdrant_points_synced_total` and sidecar state DB keep upserts idempotent |
| Concurrent index writers lose updates | Single-writer in-process serialisation; rebuild-from-markdown test as a safety net |
| Slug collisions silently overwrite distinct subjects | Deterministic disambiguation suffix + alias record |
| Over-filtering removes useful facts | Start with quarantine/rejection telemetry before destructive cleanup |
| Runtime cost increases | Batch scrub extraction, checkpoint successful turns, cap exchange size, never block interactive paths on extraction |
| Sidecar contract drift between repos | The `lk-facts` block and payload field list are documented in this PRD and mirrored in `config/indexer/README.md` (or equivalent) when the sidecar is updated |

## Open Questions

- Should quarantined wiki files live under `data/wiki/.LeanKernel/quarantine` or a visible `data/wiki/quarantine` folder?
- Should `sourceQuote` be mandatory for all extracted facts or only above a configurable confidence threshold?
- Should summaries be generated during extraction or periodically by compiler maintenance?
- How should the sidecar detect that a wiki markdown file was rewritten by `WikiCompiler` (rather than human-edited) so it can re-index without unnecessary embedding cost?

(Resolved by this revision: ownership of Qdrant sync — sidecar; index rebuild trigger — startup load + on-demand rebuild on missing/version mismatch.)

## Detailed Implementation Steps

### Step 1: Add extraction DTOs and parser

1. Add DTOs in the archivist/wiki area:
   - `WikiExtractionResponse`
   - `ExtractedWikiFact`
   - JSON serialization context entries as required by existing source-generator conventions.
2. Update `LlmWikiExtractor.ExtractionInstructions` to request a root object with `facts`.
3. Pass `response_format = { "type": "json_object" }` to LiteLLM.
4. Replace array-based parsing with object parsing.
5. Validate:
   - `facts` exists
   - `primaryDimension` maps to `WikiDimension`
   - `claim` and `subject` are specific (not in the rejection list from FR-8)
   - at least one 5W1H field is populated
   - any LLM-supplied `confidence` field is ignored
6. Log parse and validation failures with source ID and failure reason.
7. Add tests for valid JSON, fenced JSON, malformed JSON, missing fields, generic subject rejection, and ignored LLM-emitted confidence.

### Step 2: Add extractor interface

1. Add `IWikiFactExtractor` to the core interfaces or archivist abstraction layer with the DTO-returning signature from FR-2.
2. Implement it in `LlmWikiExtractor`. Keep the fire-and-forget `ExtractAsync(string, string, string)` overload for the learning pipeline.
3. Register the interface in DI.
4. Update tests to substitute the interface.

### Step 3: Extend domain models

1. Add `Summary`, `Aliases`, and `Tags` to `WikiEntry`.
2. Add `WikiFactContext`.
3. Add `Context`, `SourceQuote`, `NormalizedKey`, and `Tags` to `WikiFact`.
4. Update public API baselines if this repository requires shipped API updates.
5. Update serialization and markdown round-trip tests.

### Step 4: Add fact mapping, normalisation, and confidence

1. Create `WikiFactMapper` that converts `ExtractedWikiFact` to `WikiEntry`/`WikiFact`.
2. Normalize dimensions and subjects.
3. Generate canonical entry IDs as `{dimension}-{subject-slug}`. Apply the FR-3 disambiguation suffix on collision and record the original subject as an alias.
4. Generate `NormalizedKey` as `{entryId}|{normalized-claim}`.
5. Normalize claim keys by lowercasing, stripping punctuation, removing common stopwords, and collapsing whitespace.
6. Compute deterministic confidence per FR-8.
7. Merge facts from the same extraction response by canonical entry ID.
8. Add tests for ID stability, slug collision handling, date removal, `llm-` prefix avoidance, aliases, tags, normalized key dedupe, and confidence assignment by source type.

### Step 5: Refactor markdown serialisation

1. Update `WikiStore.SerializeToMarkdown` to emit:
   - the new YAML frontmatter fields,
   - `## Summary`, `## Facts`, the fenced `lk-facts` YAML block, `## Also Known As`, `## Related`.
2. Update `ParseMarkdown` to read frontmatter (`summary`, `aliases`, `tags`), `## Facts` (human prose only — not parsed back), the `lk-facts` block (canonical), `## Also Known As`, and `## Related`.
3. Keep backward compatibility with the existing inline HTML comment metadata format during migration only. Reads accept both; writes always emit the new format.
4. Add round-trip tests for old and new markdown formats, including edge cases with `|`, newlines, and backticks in claims and quotes.

### Step 6: Add wiki index

1. Add index model types for `WikiIndex`, `WikiIndexEntry`, and `WikiFactPointer`.
2. Store index files under `data/wiki/.LeanKernel/index.json`.
3. Add index load, rebuild, and save operations.
4. Implement the FR-4 concurrency policy: a `SemaphoreSlim` in `WikiStore` serialises all index writes; each write re-reads the latest persisted index before merging.
5. Rebuild from markdown files in the six canonical dimension folders. Exclude `.LeanKernel`, `llm`, and quarantine folders.
6. Use atomic temp-file + rename for index writes.
7. Implement startup load with synchronous rebuild on missing/version-mismatch.
8. Populate per-dimension `factPointers` from each fact's populated 5W1H fields.
9. Add tests for index creation, rebuild, invalidation, missing file handling, and concurrent-write convergence.

### Step 7: Make `WikiStore` index-aware

1. Update `ResolvePath` to use `WikiEntry.Dimension` on writes and index metadata on reads. Never derive dimension by splitting the entry ID.
2. Reject writes whose ID prefix and `WikiEntry.Dimension` conflict.
3. Update `GetAsync` to resolve via index first.
4. Update `ListByDimensionAsync` to consult `factPointers` and then load the referenced markdown bodies.
5. Update `QueryAsync` to use indexed filters and ranked candidates before loading bodies.
6. Sort query results by confidence, text relevance, recency, and access count.
7. Add tests for canonical paths, rejected unknown-dimension writes, ranked query behaviour, max-result handling, and cross-dimensional listing via `factPointers`.

### Step 8: Improve merge and quality behaviour

1. Update `IngestFactsAsync` and `MergeFacts` to use `NormalizedKey`.
2. Merge duplicate facts by:
   - max confidence (after deterministic recomputation)
   - latest confirmation date
   - union of sources, aliases, relations, and tags
3. Implement input-side gating per FR-8.
4. Add configurable ignored subjects and minimum specificity thresholds.
5. Quarantine rejected records with reason metadata.
6. Add tests for near-duplicate merge, generic subject rejection, source quote preservation, confidence updates, and input-side gating.

### Step 9: Update sidecar Qdrant projection

1. In `config/indexer/indexer.py`, extend `parse_wiki_markdown` (or add a peer parser) to extract the fenced `lk-facts` YAML block.
2. For each fact, emit:
   - one Qdrant point for the normalised claim
   - one optional point for the source quote
3. Use point IDs `uuid5(NAMESPACE_LK_WIKI, factKey + ':' + vectorType)`.
4. Extend `chunk_payload` to include FR-6 payload fields.
5. Preserve `source_type="wiki"` for compatibility with `KnowledgeSearchService` filters.
6. Add sidecar-level tests where the sidecar test harness supports them.

### Step 10: Add one-shot migration command

1. Add a `wiki-migrate` CLI/admin task (and corresponding service in C#).
2. Implement FR-7 steps in order, including the `(inferredDimension, normalizedSubject)` merge key and cross-dimension quarantine.
3. Write `migration.completed` sentinel on success.
4. In `WikiCompiler.CompileAsync`, short-circuit any migration logic when the sentinel exists.
5. Add idempotency tests and cross-dimension collision tests using fixtures derived from current `data/wiki/llm` content.

### Step 11: Remove deterministic runtime extraction

1. Remove `RegexFactExtractionStep` from `AddSelfImprovement`.
2. Update `ConversationCompactor` to call `IWikiFactExtractor` and ingest mapped results.
3. Update `ChatFactScrubJob` to call `IWikiFactExtractor`.
4. Preserve chat scrub checkpoints only after successful extract and ingest.
5. Delete or deprecate `WikiExtractor` after all runtime callers are removed.
6. Replace `WikiExtractorTests` with parser, mapper, and runtime caller tests.

### Step 12: Update consumers and UI

1. Fix `WikiQueryTool.ExecuteAsync` to deserialise `parametersJson` into `{query, dimensions[], maxResults}` and build `WikiQuery` from those fields.
2. Update `ContextCandidateRetriever` to prefer high-confidence, high-specificity facts and compact summaries.
3. Update `WikiController` to use indexed counts and queries.
4. Update `Wiki.razor` to show summaries, aliases, 5W1H context, source quotes, and related links. Display tables are display-only.
5. Keep prompt-facing outputs compact to avoid context bloat.
6. Add unit/UI tests where existing test patterns support them.

### Step 13: Validate

1. Run the existing build and tests from `src`.
2. Run the repository coverage gate.
3. Inspect migrated wiki samples for human readability.
4. Verify `data/wiki/llm` is empty or fully accounted for by migration/quarantine.
5. Verify `index.json` can be deleted and rebuilt; assert structural equality (modulo `builtAt`).
6. Verify Qdrant payloads can be regenerated from markdown by re-running the sidecar against an empty Qdrant collection.
7. Verify `WikiQueryTool` returns ranked results for a normal `{query: "..."}` envelope.

## Sprint-Ready Engineering Tickets

- [ ] `WIKI-01` Define the LLM extraction DTOs, root JSON object contract, strict JSON-mode request, parser, validation errors, and parser tests.
- [ ] `WIKI-02` Add `IWikiFactExtractor` returning `IReadOnlyList<ExtractedWikiFact>`, implement it in `LlmWikiExtractor`, register it in DI, and update extraction step tests to use the interface.
- [ ] `WIKI-03` Extend `WikiEntry`, `WikiFact`, and markdown round-tripping to support summaries, aliases, tags, source quotes, normalized keys, and `WikiFactContext`.
- [ ] `WIKI-04` Implement `WikiFactMapper` with canonical IDs, subject slugs, slug-collision disambiguation, primary dimensions, normalized claim keys, deterministic confidence, aliases, and tag merging.
- [ ] `WIKI-05` Refactor `WikiStore` serialization to the new format with the fenced `lk-facts` YAML block while preserving backward-compatible parsing of existing files.
- [ ] `WIKI-06` Add `data/wiki/.LeanKernel/index.json` with `factPointers`, in-process serialised writes, atomic temp-rename, startup load + on-demand rebuild, and index-aware `GetAsync`, `ListByDimensionAsync`, and `QueryAsync`.
- [ ] `WIKI-07` Implement quality gates (input-side and output-side), normalized-key merge behaviour, low-signal quarantine, and ranked query scoring.
- [ ] `WIKI-08` Extend the Python sidecar (`config/indexer/indexer.py`) to parse `lk-facts`, emit per-fact Qdrant points with UUID5 IDs, and include FR-6 payload fields. **No C# Qdrant writer.**
- [ ] `WIKI-09` Add `wiki-migrate` one-shot command (and short-circuit logic in `WikiCompiler.CompileAsync` when `migration.completed` exists) for migration from `data/wiki/llm`, including duplicate merge, canonical rewrite, cross-dimension quarantine, and migration logs.
- [ ] `WIKI-10` Remove deterministic runtime extraction from self-improvement, compaction, and scrub jobs once the LLM path is tested.
- [ ] `WIKI-11` Fix `WikiQueryTool` parameter parsing; update wiki controller, context retriever, and UI to use indexed retrieval and show human/LLM navigation affordances.
- [ ] `WIKI-12` Run full validation, including build, tests, coverage gate, index rebuild, migration idempotency, sidecar payload regeneration, and concurrent-ingest convergence test.

---

## Revisions Appendix

This appendix lists the material changes made during PRD review on 2026-05-13, the validated reason for each, and a citation to the codebase or to PRD lines in the original draft. Items are tagged **Tier A** (the original wording will not work) or **Tier B** (the original would work but produce poor results).

| # | Tier | Change | Validated reason | Citation |
|---|------|--------|------------------|----------|
| A1 | A | Removed C# `WikiQdrantSync` component. Re-scoped Qdrant fact-level indexing to the existing Python sidecar `config/indexer/indexer.py` (FR-6, Architecture, WIKI-08). | A second Qdrant writer would conflict with the sidecar that already watches `data/wiki/`, parses frontmatter, embeds, and upserts to the unified `LEANKERNEL_knowledge` collection. Two writers risk schema drift, duplicate point IDs, and double embedding cost. | `config/indexer/indexer.py:48-49, 401-441, 545-565, 737-...`; `src/LeanKernel.Archivist/Knowledge/KnowledgeSearchService.cs:1-36`. |
| A2 | A | FR-5 / Step 7 / AC-3 require dimension to be derived from `WikiEntry.Dimension` (writes) or the index (reads), never by string-splitting the entry ID. | `WikiStore.ResolvePath` currently splits the ID on the first `-`; subjects whose slugs start with a dimension token (`what-if-analysis`, `where-clause`, `how-to-guide`) silently mis-resolve. The PRD's `{dimension}-{subject-slug}` ID scheme inherits this hazard. | `src/LeanKernel.Archivist/Wiki/WikiStore.cs:476-483, 423-436`. |
| A3 | A | FR-2 / Step 2 changed: `IWikiFactExtractor` returns `IReadOnlyList<ExtractedWikiFact>` (DTOs), and `WikiFactMapper` builds `WikiEntry`. | The original Step 2 returned `IReadOnlyList<WikiEntry>` while Step 4 introduced a separate mapper that produces `WikiEntry` from DTOs — a contradiction. Separating concerns also keeps the LLM boundary pure. | Original PRD lines 458-472 vs. 482-490. |
| A4 | A | Canonical structured per-fact data lives in a fenced ` ```yaml lk-facts ` block, not in GFM tables. Tables remain UI-only. | GFM tables do not safely escape `|`, newlines, or backticks, all of which occur naturally in claims and source quotes. Original `## 5W1H Context` and `## Sources` tables would silently truncate or fail to round-trip. | Original PRD lines 326-336; sidecar parsing in `config/indexer/indexer.py:401-441, 450-460`. |
| A5 | A | FR-6 specifies Qdrant point IDs as `uuid5(NAMESPACE_LK_WIKI, factKey + ':' + vectorType)`. | Qdrant requires `uint64` or UUID point IDs. The original "stable point IDs derived from `factKey` and `vectorType`" would fail at upsert if interpreted as raw strings. | Original PRD line 540; Qdrant client constraint, mirrored by sidecar's existing `PointStruct` usage. |
| A6 | A | FR-4 / NFR / Step 6 mandate single-writer in-process serialisation (`SemaphoreSlim`) and re-read-before-merge for all index mutations; AC-11 verifies this with a concurrent-ingest test. | `ChatFactScrubJob` and `ConversationCompactor` both call `_wiki.IngestFactsAsync` (or `UpsertAsync`) and can run concurrently with interactive paths. Atomic temp-rename prevents torn files but not lost updates from stale snapshots. | `src/LeanKernel.Scheduler/Jobs/ChatFactScrubJob.cs:101`; `src/LeanKernel.Archivist/Sessions/ConversationCompactor.cs:60`. |
| A7 | A | FR-7 changed migration merge key from `subject` alone to `(inferredDimension, normalizedSubject)`; cross-dimension subject collisions go to quarantine, not auto-merge. | Current `data/wiki/llm` already contains the same subject (`assistant`, `agent`, `document`) across semantically distinct dimensions; merging by subject alone would collapse distinct entries. | Listing of `data/wiki/llm/` showing repeated subjects across files. |
| A8 | A | FR-9 / Step 12 / AC-9 elevate the `WikiQueryTool` parameter-parsing fix from "if needed" to a hard requirement. | `WikiQueryTool.ExecuteAsync` currently sets `TextQuery = parametersJson`, so the entire JSON envelope (e.g. `{"query":"foo"}`) is the search string. Tool-driven wiki search is broken today. | `src/LeanKernel.Plugins/BuiltIn/WikiQueryTool.cs:60-69`. |
| A9 | A | Added explicit non-goal: "Add a second Qdrant writer in C#." | Reinforces A1 so future tickets cannot re-introduce the conflict. | This PRD, Non-Goals (v1). |
| B1 | B | Removed `confidence` from the LLM extraction contract (FR-1) and added deterministic confidence assignment in FR-8 / Step 4 / AC-10. | LLM-self-rated confidence is uncalibrated and anchoring-biased; deterministic signals (user-stated > assistant-claimed, presence of source quote, agreement-across-turns) are more useful and align with the existing trust gradient in `WikiCompiler.ProcessEntry`. | `src/LeanKernel.Archivist/Wiki/WikiCompiler.cs:68-99`. |
| B2 | B | FR-1 / Step 1 require LiteLLM strict JSON mode (`response_format = { "type": "json_object" }`). | Strict JSON mode is supported and dramatically reduces malformed-JSON failures, addressing the original FR-1 risk without relying on prompt discipline. | `src/LeanKernel.Archivist/Wiki/LlmWikiExtractor.cs:102-140`. |
| B3 | B | FR-4 / FR-5 / Data Model add `factPointers` per dimension to the index so cross-dimensional `ListByDimensionAsync` does not require folder scans. | Original `primaryDimension` storage flattens 5W1H multi-indexing — queries like "all facts at place X" would need to scan every folder. Markdown stays single-source under primary dimension; only the index gains pointers. | Original PRD lines 80-87, 138-161 of `WikiStore.cs`. |
| B4 | B | FR-8 splits gates into input-side (pre-LLM) and output-side (post-LLM). | The empirically dominant noise source in `data/wiki/llm` comes from extracting from assistant scaffolding text. Input-side gates are cheaper than rejecting after an LLM call. | `data/wiki/llm/` content; `src/LeanKernel.Archivist/Sessions/ConversationCompactor.cs:43-64`. |
| B5 | B | FR-7 / Step 10 / WIKI-09 split migration out of `WikiCompiler.CompileAsync` into a `wiki-migrate` one-shot, gated by a `migration.completed` sentinel. | Periodic compile would otherwise probe the legacy `data/wiki/llm` path forever after migration completes. The sentinel keeps idempotency without ongoing wasted I/O. | `src/LeanKernel.Archivist/Wiki/WikiCompiler.cs:35-66`; `src/LeanKernel.Scheduler/Jobs/WikiMaintenanceJob.cs`. |
| B6 | B | FR-3 / Step 4 add a deterministic slug-collision policy (`-2`, `-3` suffix + alias record). | `Slugify` collapses non-alphanumerics; `"Alfero Chingono"` and `"Alfero-Chingono"` both produce `alfero-chingono`. Without a policy, distinct subjects silently overwrite each other. | `src/LeanKernel.Archivist/Wiki/LlmWikiExtractor.cs:210-212`. |
| B7 | B | FR-4 picks an explicit rebuild policy: startup load; synchronous rebuild on missing/version mismatch; otherwise dirty-mark + incremental. | "Lazy or startup" was vague. Bounding the worst-case first-query latency requires picking one policy. | Original PRD line 118. |
| B8 | B | NFR / Architecture state interactive paths must never block on extraction; extraction runs in `ChatFactScrubJob` / `ConversationCompactor` (or the existing fire-and-forget `LlmWikiExtractor.ExtractAsync` overload). | Cost protection. The runtime already supports this pattern; the PRD just had to say so. | `src/LeanKernel.Archivist/Wiki/LlmWikiExtractor.cs:59-72`. |
| B9 | B | AC-5 hardened: deletes `index.json`, rebuilds, asserts structural equality (modulo `builtAt`) — verified by an automated test. | Original NFR demanded rebuildability but provided no mechanical verification. AC-5 turns the aspiration into a contract. | Original PRD lines 405-409. |
| - | drop | `WikiEntry` mutation-semantics critique not folded into PRD. | Stylistic; no functional change. The current `record + with` pattern works correctly with `MergeFacts`. | `src/LeanKernel.Archivist/Wiki/WikiStore.cs:445-474`. |
| - | drop | Non-English content normalisation not added. | Out of scope per existing Non-Goals. | This PRD, Non-Goals (v1). |
