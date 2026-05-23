# Wiki / Knowledge Tool Unification Plan

## Background

A user-reported defect (session `signal_+****.json`) showed that when the
agent was asked "Search the wiki" for **John Doe**, the LLM picked the
`search_wiki` tool, which is currently backed by `WikiStore.QueryAsync` — a
local lexical search over `data/wiki/.meta/index.json`. That index:

- Builds its haystack from only `Subject + Summary + Aliases + Tags + FactKeys`,
  so mentions of "John" inside other entries' fact claims, source quotes, or
  5W1H context are unreachable.
- Is only refreshed on process start, `UpsertAsync`, or `DeleteAsync`. Manual
  edits to markdown files do not invalidate it.
- Duplicates a capability the sidecar `indexer` service already provides via
  Qdrant (`KnowledgeSearchService` / `search_knowledge`), which **does**
  semantically span every chunk of every wiki page.

`IWikiStore` itself is **not** redundant — it remains the structured 5W1H
read/write API used by `ContextGatekeeper`, `ContextCandidateRetriever`,
`ConversationCompactor`, `LlmFactExtractionStep`, `RegexFactExtractionStep`,
`LlmWikiExtractor`, `ChatFactScrubJob`, `WikiCompiler`,
`IdentityFileUpdateService`, `UserConfigurationStep`, `OnboardingOrchestrator`,
`WikiController`, `StatsController`, `Wiki.razor`, and `Dashboard.razor`. Only
its `QueryAsync` free-text path overlaps with Qdrant.

## Goals

1. Collapse the two free-text search paths into one Qdrant-backed pipeline.
2. Keep `IWikiStore` for everything it uniquely owns (structured CRUD, 5W1H
   merge, dimension listing, source-of-truth markdown reads).
3. Make tool selection by the LLM unambiguous for prompts like
   "search the wiki", "search your memory", "what do you know about X",
   "look it up", etc.
4. Provide a deterministic, non-search lookup tool when the agent already
   knows the canonical wiki entry it wants.

## Scope

### In scope
- Refactor `WikiQueryTool` (`search_wiki`) to call `IKnowledgeSearchService`
  with `sourceType = "wiki"`.
- Sharpen `KnowledgeSearchTool` (`search_knowledge`) and `DocumentSearchTool`
  (`search_documents`) tool descriptions/synonyms.
- Add `GetWikiEntryTool` (`get_wiki_entry`) backed by `IWikiStore`.
- Update DI registration and unit tests.
- Update relevant docs.

### Out of scope
- Changes to `IWikiStore` interface, write paths, merge semantics, or the
  underlying local `WikiIndex` (still used internally for CRUD/list/get).
- Changes to the sidecar `indexer` service or Qdrant schema.
- Changes to `ContextCandidateRetriever` (continues to call
  `IWikiStore.QueryAsync` directly for structured turn-time retrieval — its
  needs are different from the agent-tool surface).
- Changes to the wiki Blazor pages or admin controllers.

## Design

### Tool surface (after change)

| Tool | Backend | Purpose |
|---|---|---|
| `search_knowledge` | `IKnowledgeSearchService` (Qdrant, both collections) | Primary semantic search across **wiki + documents**. Default discovery tool. |
| `search_wiki` | `IKnowledgeSearchService` with `sourceType="wiki"` | Same engine, narrowed to wiki content only. |
| `search_documents` | `IKnowledgeSearchService` with `sourceType="document"` | Same engine, narrowed to documents only. (Already exists — only description sharpening.) |
| `get_wiki_entry` | `IWikiStore.GetAsync` / `ListByDimensionAsync` | Deterministic lookup of a specific structured wiki entry by id, or by `dimension + subject`. |

### Tool description / synonym strategy

The LLM should pick `search_knowledge` for any free-text recall intent —
including phrasings that historically funneled it into `search_wiki`. The
descriptions will explicitly enumerate trigger phrases:

- `search_knowledge` triggers: "search the wiki", "search your memory",
  "search your knowledge", "look up", "look it up", "do you know", "do you
  remember", "what do you know about", "find information about", "recall",
  "remember when", "have we discussed", "search documents", "search notes",
  "check the wiki", "check your notes".
- `search_wiki` triggers: explicit "wiki only", "personal wiki", "5W1H facts",
  "people I know", or post-`search_knowledge` narrowing.
- `search_documents` triggers: explicit "documents only", "books", "papers",
  "uploaded files", "reference material", or post-`search_knowledge`
  narrowing.
- `get_wiki_entry` triggers: agent already has an `entryId` (e.g. from a
  `search_knowledge` payload), or knows `dimension + subject` and wants
  the structured fact list rather than a vector chunk.

Tool *Description* (attribute-level, surfaced to LLM tool catalog) and the
runtime `Description` property will be aligned and contain the trigger phrases.

### `WikiQueryTool` (refactor)

- Drop `IWikiStore` dependency.
- Constructor: `(IKnowledgeSearchService knowledge, IOptions<LeanKernelConfig> config)`.
- Parameters schema: `query` (required), `maxResults` (1–20, default 5),
  `tags` (optional list, additive). Drop the `dimensions` parameter (not
  meaningful in vector space — entries are tagged on disk paths, not 5W1H).
- Tag resolution: requested tags ∪ `{"wiki"}` (force-include wiki tag so
  scoping always applies). If no requested tags, use
  `KnowledgeConfig.DefaultDocumentTags ∪ {"wiki"}`.
- Call `_knowledge.SearchAsync(query, effectiveTags, maxResults, ct, sourceType: "wiki")`.
- Output format mirrors `DocumentSearchTool`:
  `[i] (score: 0.92) {chunk text}`. On empty:
  `"No matching wiki content found."`

### `KnowledgeSearchTool` (description sharpening only)

- Behavior unchanged.
- Update `[ToolMetadata(..., Description = ...)]` attribute and runtime
  `Description` property to enumerate the trigger phrases above.
- Keep auto-adding `"wiki"` to agent tags (existing test contract).

### `DocumentSearchTool` (description sharpening only)

- Behavior unchanged.
- Update attribute + property descriptions to make it clearly the
  documents-only narrowing variant, with synonyms like
  "uploaded documents", "books", "papers", "reference notes",
  "files I've added".

### `GetWikiEntryTool` (new)

- New file: `src/LeanKernel.Plugins/BuiltIn/GetWikiEntryTool.cs`.
- Tool name: `get_wiki_entry`.
- Category: `ToolCategory.Wiki`.
- Constructor: `(IWikiStore wiki)`.
- Parameters schema:
  ```json
  {
    "type": "object",
    "properties": {
      "entryId": { "type": "string", "description": "Canonical wiki entry id (e.g. 'who-john-doe'). Preferred." },
      "dimension": { "type": "string", "enum": ["who","what","where","when","why","how"], "description": "5W1H dimension (required if entryId is omitted)." },
      "subject": { "type": "string", "description": "Subject string (required if entryId is omitted). Matched case-insensitively against subject and aliases." }
    }
  }
  ```
- Behavior:
  1. If `entryId` present → `wiki.GetAsync(entryId, ct)`.
  2. Else if `dimension + subject` present:
     - Build canonical id: `$"{dimension.ToLower()}-{Slugify(subject)}"`
       (use `WikiFactMapper.Slugify`). Try `GetAsync` first.
     - On null, fall back to `ListByDimensionAsync(dimension, ct)` and pick
       the first entry whose `Subject` or any `Aliases` matches `subject`
       (case-insensitive, trimmed).
  3. If neither path yields an entry, return success with output
     `"No wiki entry found for the requested key."` (mirror style of other
     tools — empty results are not errors).
  4. Else, format the entry as a compact, agent-friendly block:
     ```
     # {Subject}  (id: {Id}, dimension: {Dimension})

     {Summary}

     ## Facts
     - {Claim} (confidence: {Confidence:F2}, source: {Source})
     ...

     ## Aliases
     {alias-list}

     ## Tags
     {tag-list}
     ```
- Validation:
  - Missing both `entryId` and (`dimension` + `subject`) → `Success=false`
    with error `"Provide either 'entryId' or both 'dimension' and 'subject'."`.
  - Invalid `dimension` value → `Success=false` with descriptive error.
  - JSON parse failure → existing error pattern.
- `WikiFactMapper.Slugify` is currently `public static`; reuse it directly
  from `LeanKernel.Archivist.Wiki`. (No new public API needed.)

### DI registration

`LeanKernelFeatureServiceCollectionExtensions.AddPlugins`
(`src/LeanKernel.Host/LeanKernelFeatureServiceCollectionExtensions.cs:236`):

- Existing line `services.AddSingleton<ITool, WikiQueryTool>();` keeps working
  (constructor changed, DI resolves new params from container).
- Add `services.AddSingleton<ITool, GetWikiEntryTool>();` after the existing
  three knowledge tools.

### Public API surface

- No `IWikiStore` interface changes.
- No `IKnowledgeSearchService` interface changes.
- `WikiQueryTool` constructor signature changes (public type, but typically
  resolved via DI). Verify `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`
  files for `LeanKernel.Plugins` (likely none — only `LeanKernel.Core` is
  under PublicAPI analyzers per earlier grep showing
  `LeanKernel.Core/PublicAPI.Shipped.txt`). If unshipped tracking exists for
  Plugins, update accordingly.

## Test changes

### `LeanKernel.Tests.Unit/Plugins/WikiQueryToolTests.cs` (rewrite)

Rewrite all five tests to mock `IKnowledgeSearchService` instead of
`IWikiStore`. New cases:

1. `Name_IsSearchWiki` — unchanged assertion.
2. `ParametersSchema_HasQuery` — schema contains `"query"` and `"maxResults"`,
   does **not** contain `"dimensions"`.
3. `ExecuteAsync_WithResults_ReturnsFormatted` — stub
   `IKnowledgeSearchService.SearchAsync` returning one `RelevanceScore`;
   assert output contains chunk content and score.
4. `ExecuteAsync_NoResults_ReturnsMessage` — empty list →
   `"No matching wiki content found."`.
5. `ExecuteAsync_InvalidJson_ReturnsError` — same as today.
6. `ExecuteAsync_ServiceThrows_ReturnsError` — propagates exception message.
7. **New:** `ExecuteAsync_AlwaysScopesToWikiSourceType` — verify
   `_knowledge.SearchAsync` is called with `sourceType: "wiki"` and tags
   include `"wiki"`.
8. **New:** `ExecuteAsync_HonorsCustomMaxResultsAndTags` — clamps
   `maxResults` to 20 and unions caller tags with `"wiki"`.

### `LeanKernel.Tests.Unit/Plugins/GetWikiEntryToolTests.cs` (new)

1. `Name_IsGetWikiEntry`.
2. `ParametersSchema_AllowsEntryIdOrDimensionSubject`.
3. `ExecuteAsync_ByEntryId_ReturnsFormattedEntry` — stub
   `wiki.GetAsync("who-jane-doe", _)` returning a `WikiEntry`; assert output
   contains subject, dimension, facts, aliases.
4. `ExecuteAsync_ByDimensionAndSubject_BuildsSlugAndCallsGet`.
5. `ExecuteAsync_FallsBackToListByDimension_WhenSlugMisses` — `GetAsync`
   returns null, `ListByDimensionAsync` returns matching entry by alias.
6. `ExecuteAsync_NotFound_ReturnsFriendlyMessage` (success=true, output
   contains `"No wiki entry found"`).
7. `ExecuteAsync_MissingBothIdentifiers_ReturnsError`.
8. `ExecuteAsync_InvalidDimension_ReturnsError`.
9. `ExecuteAsync_InvalidJson_ReturnsError`.
10. `ExecuteAsync_StoreThrows_ReturnsError`.

### `LeanKernel.Tests.Unit/Plugins/KnowledgeSearchToolTests.cs`

- Existing tests remain valid (behavior unchanged). Optionally add an
  assertion that the description string contains a representative trigger
  phrase (e.g. `"search your memory"`) to lock in the contract.

### `LeanKernel.Tests.Unit/Plugins/DocumentSearchToolTests.cs`
(verify whether this test file exists; create or extend as needed for parity.)

## Documentation updates

- `docs/features/wiki.md` (or closest existing wiki/knowledge feature doc):
  add a "Tool surface" subsection describing the four tools and when each
  is selected. Reference `search_knowledge` as the default.
- `docs/skills/` if any skill explicitly mentions `search_wiki`'s old
  dimension parameter, update.
- `README.md` — only if it lists the tool inventory; skim and update if so.
- `docs/plans/index.md` — add a link to this plan.

## Implementation steps (suggested order)

1. **Refactor `WikiQueryTool`** to use `IKnowledgeSearchService`. Build.
2. **Sharpen** `KnowledgeSearchTool` + `DocumentSearchTool` descriptions.
3. **Add `GetWikiEntryTool`** + register in `AddPlugins`.
4. **Rewrite `WikiQueryToolTests`**; add `GetWikiEntryToolTests`.
5. Run `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
6. Run `dotnet test src/LeanKernel.sln --no-build -v minimal`.
7. Run `scripts/quality/test-coverage.sh` to confirm coverage gate
   (≥81.83% line baseline).
8. Run `docker compose build` to confirm container build still succeeds.
9. Run `scripts/quality/sonarqube-scan.sh`.
10. Update docs (`docs/features/wiki.md`, `docs/plans/index.md`).
11. Manual smoke: replay the failing prompt — "Do you know who John
    Doe is?" → "Search the wiki." — and verify the agent now selects
    `search_knowledge` (or `search_wiki` correctly hitting Qdrant) and
    surfaces cross-page mentions.

## Risk and mitigation

| Risk | Mitigation |
|---|---|
| Existing callers / tests rely on `WikiQueryTool`'s `dimensions` parameter. | Grep confirmed only the unit test references it; no production callers. The new schema simply omits it — JSON validators on tool inputs ignore unknown properties. |
| Qdrant collection empty in dev environments → `search_wiki` returns nothing. | Same risk affects `search_knowledge` already. Add a debug log line via existing `_logger` patterns when no collection exists (already handled in `KnowledgeSearchService`). Documented as deployment prerequisite. |
| LLM still picks `search_wiki` for free-text intents and gets narrower results than `search_knowledge`. | Acceptable — `search_wiki` now hits the same Qdrant engine, just narrowed by `source_type`. Cross-page mentions of subjects within the wiki collection are still surfaced. |
| Slug-based id construction in `get_wiki_entry` mismatches actual on-disk id (e.g. unicode subjects, trailing punctuation). | Always fall back to `ListByDimensionAsync` + case-insensitive subject/alias match. |
| Tool description changes might regress prompt-routing benchmarks (if any exist under `docs/plans/benchmark-scenarios-prd.md`). | Run any existing benchmark suite; if a routing benchmark exists for this prompt class, capture before/after numbers. |

## Acceptance criteria

- [ ] `search_wiki` no longer depends on `IWikiStore`; depends on
      `IKnowledgeSearchService`.
- [ ] `search_wiki` calls `SearchAsync` with `sourceType="wiki"` and tags
      including `"wiki"` (verified by unit test).
- [ ] `search_documents` description enumerates documents-only synonyms.
- [ ] `search_knowledge` description enumerates trigger phrases including
      "search the wiki", "search your memory", "do you remember", "look up".
- [ ] `get_wiki_entry` exists, registered in DI, supports `entryId` and
      `dimension+subject` lookups, with alias fallback.
- [ ] All existing tests pass; new tests added for both refactored and new
      tools.
- [ ] `docker compose build` succeeds.
- [ ] Coverage ≥ baseline (81.83%).
- [ ] SonarQube scan passes.
- [ ] `docs/features/wiki.md` and `docs/plans/index.md` updated.

## Validation sequence

```bash
dotnet restore src/LeanKernel.sln
dotnet build   src/LeanKernel.sln --no-restore -v minimal
dotnet test    src/LeanKernel.sln --no-build    -v minimal
scripts/quality/test-coverage.sh
docker compose build
scripts/quality/sonarqube-scan.sh
```

## Open questions

1. Should `get_wiki_entry` also accept a list of `entryId`s for batch hydration
   after a `search_knowledge` call returns multiple chunks pointing at
   different entries? (Recommended: yes, second iteration — keep this PR
   scoped to single-entry lookup.)
2. Should the orchestrator/router auto-fall-back from `search_wiki` →
   `search_knowledge` when results are empty? (Recommended: not in this PR;
   tool description sharpening is the cheaper first lever.)
3. Should `WikiQueryTool` re-rank vector results using `IWikiStore`
   confidence/recency metadata? (Recommended: defer — `KnowledgeSearchService`
   already returns Qdrant similarity scores; structured re-rank is a separate
   feature.)
