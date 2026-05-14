# PRD: LLM Wiki Extraction and Indexed Wiki Store

## Overview

Replace deterministic wiki fact extraction with an LLM-backed extraction pipeline that produces structured 5W1H facts, then refactor `WikiStore` into a human-readable, file-backed database with durable indexes and Qdrant-ready semantic retrieval.

This PRD is an engineering planning document for developers implementing the wiki extraction, storage, migration, and retrieval changes.

## Problem Statement

The current wiki has low signal and inconsistent storage. Most generated files are under `data/wiki/llm`, while canonical 5W1H folders are mostly empty. LLM-generated IDs use `llm-...` prefixes, and `WikiStore.ResolvePath` interprets the first ID segment as a directory, so entries can be physically stored outside their declared dimension.

The deterministic `WikiExtractor` is still active in the learning pipeline, conversation compaction, and chat scrub job. It produces shallow regex facts that are often less useful than the surrounding conversation. The existing LLM extractor is better, but it returns a flat array with one dimension per item and does not preserve all six 5W1H dimensions for each discovered fact.

The store also behaves like a folder of markdown files rather than a small database. Queries scan files, dedupe is exact-string based, metadata is hidden in inline HTML comments, and there is no durable index for fast lookup, Qdrant sync, migration, aliases, or LLM-friendly navigation.

## Goals

- Replace deterministic fact extraction with one LLM-backed extraction contract.
- Preserve all six 5W1H dimensions for every discovered fact.
- Keep wiki records easy for humans to read as markdown.
- Make wiki records easy for an LLM to navigate during analysis.
- Make wiki facts indexable by Qdrant at fact-level granularity.
- Refactor `WikiStore` into an indexed, file-backed store with stable IDs and atomic writes.
- Migrate existing `data/wiki/llm` entries into canonical 5W1H folders.
- Improve signal-to-noise through quality gates, dedupe, pruning, and ranked retrieval.

## Non-Goals (v1)

- Replace markdown storage with a relational database.
- Depend on Qdrant as the source of truth.
- Build a full admin curation workflow for manual fact review.
- Implement cross-language semantic normalization beyond deterministic English-first normalization.
- Automatically delete high-confidence facts without migration logs or quality safeguards.

## User Stories

- As a developer, I can inspect wiki markdown files and understand facts, sources, aliases, and relationships without reading hidden metadata.
- As the LLM, I can discover useful facts by scanning summaries, aliases, dimension folders, and related links.
- As the retrieval layer, I can search Qdrant at fact-level precision instead of embedding whole wiki entries.
- As an operator, I can rebuild indexes and Qdrant projections from markdown if generated state is lost.
- As the system, I can reject vague process noise and prefer specific, grounded, high-confidence facts.

## Functional Requirements

### FR-1 LLM extraction contract

`LlmWikiExtractor` must request and parse a single JSON object with a `facts` array. Each fact must include:

- `who`
- `what`
- `when`
- `where`
- `why`
- `how`
- `claim`
- `subject`
- `primaryDimension`
- `confidence`
- `sourceQuote`
- `summaryHint`
- `aliases`
- `tags`

The parser must reject malformed JSON, invalid dimensions, out-of-range confidence scores, blank claims, generic subjects, and ungrounded facts that lack a useful claim or source quote. Invalid responses must be logged rather than silently swallowed.

### FR-2 Extraction interface

Runtime callers must depend on an extraction interface, such as `IWikiFactExtractor`, rather than the concrete `LlmWikiExtractor`.

Callers to update:

- `LlmFactExtractionStep`
- `ConversationCompactor`
- `ChatFactScrubJob`

The deterministic extractor must remain only until the LLM interface, parser, mapping, and indexed store path are stable and tested.

### FR-3 Canonical markdown records

The wiki must store one canonical markdown file per subject and primary dimension:

```text
data/wiki/{dimension}/{subject-slug}.md
```

Canonical files must not include extraction dates or `llm-` prefixes. Dates and source IDs belong in fact metadata, not entry IDs or filenames.

Each markdown file must be readable in a terminal and render clearly in GitHub. Long-term storage should replace inline HTML comment metadata with visible markdown sections:

- frontmatter
- `# {Subject}`
- `## Summary`
- `## Facts`
- `## 5W1H Context`
- `## Sources`
- `## Also Known As`
- `## Related`

### FR-4 File-backed index

`WikiStore` must maintain generated metadata in `data/wiki/.LeanKernel/`:

- `index.json`
- `migration.json`
- optional `qdrant-sync.json`

`index.json` must include enough metadata for fast filtering and ranked lookup without parsing every markdown body on every query.

### FR-5 Database-like store behavior

`WikiStore` must support:

- dimension-safe path resolution
- stable canonical IDs
- atomic markdown writes
- atomic index writes
- lazy or startup index rebuild
- index invalidation when markdown changes
- indexed `GetAsync`
- indexed `ListByDimensionAsync`
- indexed and ranked `QueryAsync`
- merge by normalized fact key
- migration from old `llm` storage

Unknown top-level dimensions such as `llm` must not be used for new writes.

### FR-6 Qdrant fact indexing

Qdrant sync must index individual facts, not whole entries.

For each `WikiFact`:

- create one vector for the normalized claim
- create one optional vector for the source quote when present

Payload fields must include:

- `entryId`
- `factKey`
- `vectorType`
- `dimension`
- `subject`
- `subjectSlug`
- `claim`
- `sourceQuote`
- `confidence`
- `lastConfirmed`
- `source`
- `tags`

The markdown wiki and `index.json` must be sufficient to rebuild the Qdrant collection.

### FR-7 Migration

`WikiCompiler.CompileAsync` must become the single maintenance entry point. It should detect old `data/wiki/llm` files, migrate valid entries to canonical dimension folders, record migration state, rebuild the index, and prepare Qdrant sync metadata.

Migration must be idempotent. Re-running it must not duplicate facts or inflate confidence.

### FR-8 Quality gates

The store and extractor must reject or quarantine low-signal facts, including:

- `Unknown` subjects
- generic subjects such as `action`, `information`, `document`, or `file`
- claims such as `Files updated successfully`
- claims without user-relevant 5W1H context
- duplicate or near-duplicate facts
- facts below configured confidence or specificity thresholds

### FR-9 Consumer updates

Consumers must use indexed and ranked retrieval:

- `WikiQueryTool`
- `ContextCandidateRetriever`
- `WikiController`
- `Wiki.razor`

Tool and prompt output should include fewer, higher-quality facts. UI/admin detail views may show richer provenance and 5W1H context.

## Non-Functional Requirements

- Markdown remains the canonical source of truth.
- Generated indexes and Qdrant state are rebuildable projections.
- File writes must be atomic enough to avoid partial records after process interruption.
- Query behavior must avoid scanning every markdown file when the index is valid.
- Migration must be safe to run more than once.
- LLM extraction failures must not advance scrub checkpoints.
- New query and ingest paths must preserve existing public behavior where possible.

## Architecture

| Component | Responsibility |
| --------- | -------------- |
| `IWikiFactExtractor` | Interface for extracting structured 5W1H facts from turns |
| `LlmWikiExtractor` | Calls LiteLLM, parses root JSON object, validates facts |
| `WikiFactMapper` | Converts extraction DTOs into canonical `WikiEntry` and `WikiFact` records |
| `WikiStore` | File-backed indexed store over markdown records |
| `WikiIndex` | In-memory and persisted projection of wiki entry metadata |
| `WikiCompiler` | Maintenance entry point for migration, pruning, dedupe, and index rebuild |
| `WikiQdrantSync` | Builds fact-level vector payloads and syncs Qdrant projections |
| `WikiQueryTool` | Tool-facing search over ranked wiki retrieval |
| `ContextCandidateRetriever` | Prompt context retrieval using indexed/Qdrant-aware wiki candidates |

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
      "confidence": 0.92,
      "sourceQuote": "I prefer concise responses.",
      "summaryHint": "Alfero prefers concise and direct assistant responses.",
      "aliases": ["Alfero"],
      "tags": ["communication", "preference"]
    }
  ]
}
```

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
  "version": 1,
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
  ]
}
```

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

## 5W1H Context

| Claim | Who | What | When | Where | Why | How |
|---|---|---|---|---|---|---|
| Alfero Chingono prefers concise, direct assistance. | Alfero Chingono | Communication preference |  |  | Reduce response noise | Use concise answers |

## Sources

| Claim | Confidence | Source | Source Quote | Confirmed | Normalized Key |
|---|---:|---|---|---|---|
| Alfero Chingono prefers concise, direct assistance. | 0.92 | conversation:... | I prefer concise responses. | 2026-05-10 | who-alfero-chingono\|prefer-concise-direct-assistance |

## Also Known As

- Alfero
- Chingono Alfero

## Related

- [what: Communication preferences](../what/communication-preferences.md)
```

## API Requirements

Existing API contracts can remain, but returned models should include new fields once the domain model is extended.

| Method | Endpoint | Requirement |
| ------ | -------- | ----------- |
| GET | `/api/wiki/dimensions` | Use indexed counts and fact totals |
| GET | `/api/wiki/entries` | Use ranked indexed query results |
| GET | `/api/wiki/entries/{entryId}` | Resolve via canonical index and return full record |

## UI Requirements

The wiki page should remain a human browsing tool and add fields that help evaluate quality:

- summary preview on entry cards
- aliases in entry detail
- 5W1H context table
- source quote and confidence table
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
- `wiki_qdrant_points_synced_total`
- Reduction in `data/wiki/llm` files to zero after migration
- Increase in query precision for wiki-backed context

## Rollout Plan

1. Phase 0: add DTOs, parser, mapper tests, and extraction interface.
2. Phase 1: add indexed `WikiStore` behavior while keeping current markdown compatibility.
3. Phase 2: add visible markdown metadata sections and index rebuild.
4. Phase 3: add Qdrant fact payload generation and sync state.
5. Phase 4: run idempotent migration from `data/wiki/llm`.
6. Phase 5: remove deterministic runtime extraction.
7. Phase 6: update UI/API/tool consumers and tune ranking/quality thresholds.

## Acceptance Criteria

- AC-1: Runtime fact extraction no longer uses deterministic regex logic.
- AC-2: LLM extraction parses a root JSON object with all six 5W1H fields per fact.
- AC-3: New wiki files are written under canonical dimension folders with stable IDs.
- AC-4: Markdown files are readable and navigable without hidden HTML metadata.
- AC-5: `data/wiki/.LeanKernel/index.json` can be rebuilt from markdown and powers indexed queries.
- AC-6: Qdrant payloads are generated per fact claim and per source quote when present.
- AC-7: Migration moves valid `data/wiki/llm` records into canonical folders and is idempotent.
- AC-8: Low-signal generic entries are rejected or quarantined.
- AC-9: Existing build, unit tests, and coverage gate pass after implementation.

## Dependencies

- LiteLLM chat completions configuration.
- Existing `IWikiStore`, `WikiEntry`, and `WikiFact` models.
- Existing embedding/Qdrant integration or a new sync service if Qdrant sync is not yet present.
- `WikiCompiler` scheduled maintenance.
- Current wiki UI, controller, and query tool consumers.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| LLM emits malformed JSON | Typed parser, strict validation, logged rejection, no checkpoint advancement on failure |
| Migration corrupts useful data | Idempotent migration log, quarantine path, canonical merge tests |
| Markdown parser becomes brittle | Versioned markdown format and round-trip tests |
| Qdrant and markdown drift | Treat markdown/index as source of truth and make Qdrant rebuildable |
| Over-filtering removes useful facts | Start with quarantine/rejection telemetry before destructive cleanup |
| Runtime cost increases | Batch scrub extraction, checkpoint successful turns, and cap exchange size |

## Open Questions

- Should quarantined wiki files live under `data/wiki/.LeanKernel/quarantine` or a visible `data/wiki/quarantine` folder?
- Should Qdrant sync be part of `WikiCompiler` or a separate background service triggered by index changes?
- Should `sourceQuote` be mandatory for all extracted facts or only for confidence above a threshold?
- Should summaries be generated during extraction or periodically by compiler maintenance?

## Detailed Implementation Steps

### Step 1: Add extraction DTOs and parser

1. Add DTOs in the archivist/wiki area:
   - `WikiExtractionResponse`
   - `ExtractedWikiFact`
   - any JSON serialization context needed by existing conventions
2. Update `LlmWikiExtractor.ExtractionInstructions` to request a root object with `facts`.
3. Replace array-based parsing with object parsing.
4. Validate:
   - `facts` exists
   - `primaryDimension` maps to `WikiDimension`
   - `confidence` is between 0 and 1
   - `claim` and `subject` are specific
   - at least one 5W1H field is populated
5. Log parse and validation failures with source ID and failure reason.
6. Add tests for valid JSON, fenced JSON, malformed JSON, missing fields, invalid confidence, and generic subject rejection.

### Step 2: Add extractor interface

1. Add `IWikiFactExtractor` to the core interfaces or archivist abstraction layer.
2. Give it an awaitable method such as:

   ```csharp
   Task<IReadOnlyList<WikiEntry>> ExtractAsync(
       string userMessage,
       string assistantResponse,
       string sourceId,
       CancellationToken ct);
   ```

3. Implement it in `LlmWikiExtractor`.
4. Keep `ExtractAndIngestAsync` as a convenience wrapper only if needed.
5. Register the interface in DI.
6. Update tests to substitute the interface.

### Step 3: Extend domain models

1. Add `Summary`, `Aliases`, and `Tags` to `WikiEntry`.
2. Add `WikiFactContext`.
3. Add `Context`, `SourceQuote`, `NormalizedKey`, and `Tags` to `WikiFact`.
4. Update public API baselines if this repository requires shipped API updates.
5. Update serialization and markdown round-trip tests.

### Step 4: Add fact mapping and normalization

1. Create a mapper that converts `ExtractedWikiFact` to `WikiEntry`.
2. Normalize dimensions and subjects.
3. Generate canonical entry IDs as `{dimension}-{subject-slug}`.
4. Generate `NormalizedKey` as `{entryId}|{normalized-claim}`.
5. Normalize claim keys by lowercasing, stripping punctuation, removing common stopwords, and collapsing whitespace.
6. Merge facts from the same extraction response by canonical entry ID.
7. Add tests for ID stability, date removal, `llm-` prefix avoidance, aliases, tags, and normalized key dedupe.

### Step 5: Refactor markdown serialization

1. Update `WikiStore.SerializeToMarkdown` to emit the new visible markdown sections.
2. Update `ParseMarkdown` to read:
   - frontmatter summary, aliases, tags
   - `## Facts`
   - `## 5W1H Context`
   - `## Sources`
   - `## Also Known As`
   - `## Related`
3. Keep backward compatibility with existing inline HTML comment metadata during migration.
4. Prefer writing only the new format for new or updated files.
5. Add round-trip tests for old and new markdown formats.

### Step 6: Add wiki index

1. Add index model types for `WikiIndex` and `WikiIndexEntry`.
2. Store index files under `data/wiki/.LeanKernel/index.json`.
3. Add index load, rebuild, and save operations.
4. Rebuild from markdown files in the six canonical dimension folders.
5. Exclude `.LeanKernel`, `llm`, and quarantine folders from canonical scans.
6. Use atomic writes for the index.
7. Add tests for index creation, rebuild, invalidation, and missing file handling.

### Step 7: Make `WikiStore` index-aware

1. Update `ResolvePath` to use `WikiEntry.Dimension` on writes and index metadata on reads.
2. Reject new writes whose ID dimension and `WikiEntry.Dimension` conflict.
3. Update `GetAsync` to resolve via index first.
4. Update `ListByDimensionAsync` to use indexed entries and then read selected markdown bodies.
5. Update `QueryAsync` to use indexed filters and ranked candidates before loading bodies.
6. Sort query results by confidence, text relevance, recency, and access count.
7. Add tests for canonical paths, unknown dimensions, ranked query behavior, and max result handling.

### Step 8: Improve merge and quality behavior

1. Update `IngestFactsAsync` and `MergeFacts` to use `NormalizedKey`.
2. Merge duplicate facts by:
   - max confidence
   - latest confirmation date
   - union of sources, aliases, relations, and tags
3. Add configurable ignored subjects and minimum specificity thresholds.
4. Quarantine rejected records with reason metadata.
5. Add tests for near-duplicate merge, generic subject rejection, source quote preservation, and confidence updates.

### Step 9: Add Qdrant sync projection

1. Add a service that converts `WikiEntry` and `WikiFact` records into Qdrant point payloads.
2. Use stable point IDs derived from `factKey` and `vectorType`.
3. Create claim vectors and optional quote vectors.
4. Store sync state under `data/wiki/.LeanKernel/qdrant-sync.json` or equivalent.
5. Make sync rebuildable from markdown plus `index.json`.
6. Add payload generation tests and integration tests where a Qdrant test dependency already exists.

### Step 10: Add migration through `WikiCompiler`

1. Add migration orchestration to `WikiCompiler.CompileAsync`.
2. Detect `data/wiki/llm` files.
3. Parse old markdown using backward-compatible parser logic.
4. Infer canonical dimension from frontmatter, not directory.
5. Drop date suffixes and `llm-` prefixes from canonical IDs.
6. Merge duplicate subject files into canonical entries.
7. Quarantine low-signal files with explicit reasons.
8. Record source path, target entry ID, migrated fact keys, and timestamp in `migration.json`.
9. Rebuild `index.json` after migration.
10. Add idempotency tests.

### Step 11: Remove deterministic runtime extraction

1. Remove `RegexFactExtractionStep` from `AddSelfImprovement`.
2. Update `ConversationCompactor` to call `IWikiFactExtractor` and ingest results.
3. Update `ChatFactScrubJob` to call `IWikiFactExtractor`.
4. Preserve chat scrub checkpoints only after successful extract and ingest.
5. Delete or deprecate `WikiExtractor` after all runtime callers are removed.
6. Replace `WikiExtractorTests` with parser, mapper, and runtime caller tests.

### Step 12: Update consumers and UI

1. Update `WikiQueryTool` to parse its parameter JSON if needed and use ranked indexed queries.
2. Update `ContextCandidateRetriever` to prefer high-confidence, high-specificity facts and compact summaries.
3. Update `WikiController` to use indexed counts and queries.
4. Update `Wiki.razor` to show summaries, aliases, 5W1H context, source quotes, and related links.
5. Keep prompt-facing outputs compact to avoid context bloat.
6. Add unit/UI tests where existing test patterns support them.

### Step 13: Validate

1. Run the existing build and tests from `src`.
2. Run the repository coverage gate.
3. Inspect migrated wiki samples for human readability.
4. Verify `data/wiki/llm` is empty or fully accounted for by migration/quarantine.
5. Verify `index.json` can be deleted and rebuilt.
6. Verify Qdrant payloads can be regenerated from markdown and index state.

## Sprint-Ready Engineering Tickets

- [ ] `WIKI-01` Define the LLM extraction DTOs, root JSON object contract, strict parser, validation errors, and parser tests.
- [ ] `WIKI-02` Add `IWikiFactExtractor`, implement it in `LlmWikiExtractor`, register it in DI, and update extraction step tests to use the interface.
- [ ] `WIKI-03` Extend `WikiEntry`, `WikiFact`, and markdown round-tripping to support summaries, aliases, tags, source quotes, normalized keys, and `WikiFactContext`.
- [ ] `WIKI-04` Implement DTO-to-wiki mapping with canonical IDs, subject slugs, primary dimensions, normalized claim keys, aliases, and tag merging.
- [ ] `WIKI-05` Refactor `WikiStore` serialization to the visible markdown format while preserving backward-compatible parsing of existing files.
- [ ] `WIKI-06` Add `data/wiki/.LeanKernel/index.json`, index load/rebuild/save, atomic writes, and index-aware `GetAsync`, `ListByDimensionAsync`, and `QueryAsync`.
- [ ] `WIKI-07` Implement quality gates, normalized-key merge behavior, low-signal quarantine, and ranked query scoring.
- [ ] `WIKI-08` Add Qdrant fact-level payload generation and sync state that can be rebuilt from markdown and `index.json`.
- [ ] `WIKI-09` Add idempotent migration from `data/wiki/llm` through `WikiCompiler.CompileAsync`, including duplicate merge, canonical rewrite, quarantine, and migration logs.
- [ ] `WIKI-10` Remove deterministic runtime extraction from self-improvement, compaction, and scrub jobs once the LLM path is tested.
- [ ] `WIKI-11` Update wiki tool, context retrieval, controller, and UI to use indexed/Qdrant-aware retrieval and show human/LLM navigation affordances.
- [ ] `WIKI-12` Run full validation, including build, tests, coverage gate, index rebuild, migration idempotency, and Qdrant payload regeneration.
