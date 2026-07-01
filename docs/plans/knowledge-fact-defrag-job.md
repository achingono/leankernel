# Knowledge Fact Defrag Job Design

## Goal
Add a scheduled maintenance task that behaves like a wiki/fact defragmenter:
- detect older fact pages that have been superseded by newer pages
- retire stale facts instead of deleting them
- preserve an audit trail in the retired page content
- enforce a consistent 5W1H (`Who`, `What`, `When`, `Where`, `Why`, `How`) section on fact pages

## Scope
This design targets learned fact pages (`# Learned Fact`) discovered through `IKnowledgeService.SearchAsync`.

The initial implementation is deterministic and rule-based (no additional LLM dependency):
- explicit supersession metadata (`- Supersedes:` and `- SupersededBy:`)
- duplicate fact content detection (normalized fact text)

## Task contract
Use scheduler job type `maintenance` with:

- `task=knowledge-fact-defrag`
- optional `scope_query` (default `learning/facts/`)
- optional `max_candidates` (default `200`, clamped to `1000`)
- optional `min_age_days` (default `14`)
- optional `normalization_mode` (`hybrid` default, `deterministic` alternative)
- optional `normalization_context_mode` (`related-pages` default, `isolated` alternative)
- optional related-page limits (`related_pages_max`, `same_session_max`, `semantic_neighbors_max`)
- optional `max_llm_repairs_per_run` (bounds hybrid LLM repair volume)

## Algorithm
1. Search knowledge candidates using `scope_query` and `max_candidates`.
2. Load each page and keep only learned/retired fact page shapes.
3. Build active fact set (exclude already retired pages).
4. Plan retirements from:
   - explicit supersession metadata
   - duplicate normalized fact text, keeping newest page as canonical
5. Apply `min_age_days` cutoff before retirement.
6. Retire pages by rewriting them as `# Retired Fact` with:
   - `Status`, `RetiredAt`, `RetirementReason`, `SupersededBy`
   - original fact text
   - original full content snapshot
7. Normalize all scanned fact pages into a 5W1H shape:
   - active pages become `# Learned Fact` + `## 5W1H`
   - retired pages keep retired status and include `## 5W1H`
   - missing fields are not silently defaulted; page is marked `NormalizationStatus: partial`
8. In hybrid mode, attempt LLM repair on partial pages up to `max_llm_repairs_per_run`.
9. In `related-pages` context mode, provide deterministic evidence pack from related pages (links, same session, semantic neighbors).

## Review notes
- Safety: no hard delete; retirement is idempotent and reversible.
- Auditability: scheduler execution is still persisted to `ScheduledJobExecutions`.
- Backward compatibility: existing cleanup maintenance tasks are unchanged.
- Operational behavior: appsettings promotes `knowledge-maintenance` to defrag and adds `engine-maintenance` for old diagnostics cleanup.

## Follow-ups
- Add stronger relation heuristics (claim/entity overlap) beyond exact duplicate matching.
- Add a dry-run mode (`apply=false`) to report planned retirements without writes.
- Surface retirement metrics on diagnostics/admin views.
