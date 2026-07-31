# Phase 2026-07-31 Activities

Symbols used below (resolved from inputs at runtime; current dev examples in `inputs.md`):
- `LT` = target local tenant id
- `LP` = target local person id
- `LU` = target local user id
- `CH_MEM` = target local memory-write channel id (current convention: openai-http)
- `CH_ALL` = discovered active local channel ids / permitted readable channels used for validation
- Local DB: `localhost:5432` DB `gbrain` user `leankernel`; remote: `192.168.*.*:5432` DB `leankernel` (creds from swarm secrets, never echoed)

## Step-By-Step Activities

0. **Plan review.** Review this plan with a separate model/session before implementation.

1. **Resolve import targets and local channel set.** Before any mutation, resolve and record the operator-approved local target tenant/person/user mapping and discover the active local channel set from the local `Channels` table/config used by the running stack. Fail if the target mapping is ambiguous or if no active channel set can be derived. This turns the current dev GUIDs into environment-specific inputs instead of script constants.

2. **Backup local target.** Full dump of the local `gbrain` database to `scripts/gbrain/backups/gbrain-pre-import-<timestamp>.sql` (via `docker compose exec -T database pg_dump`). This is the rollback path.

3. **Export remote GBrain data.** From the `database` container (`pg_dump` 16 available; remote is Postgres 16.14):
   - `docker compose exec -T database pg_dump -h 192.168.*.* -U "$(cat /tmp/postgres_user.txt)" -d leankernel --data-only --no-owner --table=pages --table=content_chunks --table=page_versions --table=tags --table=page_aliases` (PGPASSWORD read from the secrets file inside the container; credentials never appear in command lines or logs).
   - Store the dump under `scripts/gbrain/backups/remote-gbrain-<timestamp>.sql`.
   - Record a manifest (counts per table) from the dump for verification.

4. **Restore into a staging database first.** Create `gbrain_import_staging` in the local Postgres (schema from local `gbrain` via `pg_dump --schema-only`), restore the remote data into it, and run the whole rewrite + re-embed flow against staging. Only after staging verification, repeat against the real `gbrain` DB. This isolates R6 (embedding regeneration) and R7 (GBrain CLI behavior on null vectors) from production local data.

5. **Restore + sequence fixup.** `pg_dump --data-only` carries explicit ids but not sequence state; after restore run `setval(pg_get_serial_sequence(...), max(id))` for every restored table (`pages`, `content_chunks`, `page_versions`, `tags`, `page_aliases`). Row-level security on `page_versions` is bypassed for the table owner (`leankernel`), so owner-based restore is fine.

6. **Implement user-scoped document retrieval before final import.** Update the document retrieval path so imported documents can live under `DocumentAvailabilityScope.User` and still be discoverable from every permitted channel. **Done (2026-07-31, store-only change):** verified against the deployed gbrain 0.42.67.0 MCP surface that `search` accepts only `query`/`limit`/`offset`/`mode` — the `ns` param was silently ignored, so searches are brain-wide and client-side filtering was already the real authorization gate. `GBrainDocumentStoreClient` now: (a) filters by slug shape — `documents/{tenant}/user/{channel-or-Guid.Empty}/{user}/{fp}` passes for the requesting user regardless of channel; `documents/{tenant}/channel|tenant/...` requires channel membership; memory/probe/unknown shapes are dropped; tenant must match the request tenant; (b) merges by fingerprint (last slug segment): `document_search` keeps the highest score per fingerprint with lexical slug asc as the deterministic tie-break (search payloads carry no timestamps), `document_list` calls `list_pages` (`sort=updated_desc`, headroom 3×, remote cap 100) and keeps the newest `updated_at` per fingerprint then slug asc; (c) fetch limit is 3× so merged results still fill `maxResults`. Tools are unchanged — channel authorization still comes from policy via the readable-channel set. Unit tests (12) cover canonical user-scope surfacing, dedupe highest-score, lexical tie-breaks, other-user/other-tenant drops, non-document shape drops, and `list_pages` ordering.

7. **Slug rewrite — dry run first.** Script must emit, without mutating: per-category counts, first/last N old→new mapping samples, collision analysis (see R4), channel-discovery output (`CH_MEM`, `CH_ALL`), and a full mapping manifest CSV (`old_slug,new_slug,type,channel_copy,collision_strategy`). Fail on unexpected slug shapes (anything outside categories A/B/C or `memory/`/`documents/` prefixes already present locally) instead of guessing.

8. **Apply slug rewrite (SQL pass).** Mapping:

   | Category | Old slug | New slug | Copies |
   |---|---|---|---|
   | A — learned facts (1,434) | `learning/facts/{t}/{p}/{seq}` | `memory/{LT}/{LP}/{CH_MEM}/learning/facts/{t}/{p}/{seq}` | 1 |
   | B — documents (1,589) | `doc/{title}` | `documents/{LT}/user/{Guid.Empty}/{LU}/{fp}` where `fp` = lowercase hex SHA-256 of `compiled_truth` (matches `DocumentLibraryService` fingerprint convention) | 1 — a single canonical user-scoped copy; retrieval is authorized per channel but resolved through user scope |
   | C — raw slugs (125) | `what/budget`, `how/network`, `identity-user-default`, `learning/engagement-metrics`, … | `memory/{LT}/{LP}/{CH_MEM}/{original slug}` | 1 |

   Notes:
   - Category B: keep `type='document'`; preserve the original remote title slug in metadata/manifest and set `title` deterministically so repeated runs are stable. Page content stays the raw markdown (`compiled_truth`) — search excerpts then carry real text (better than the JSON-blob storage used by `UpsertAsync`). Rewrite the restored row in place to a single canonical user-scoped slug; do not insert per-channel duplicates.
   - Category B retrieval behavior: after the document tool/store change, `document_search` and `document_list` must authorize by the current channel's permitted scope set but resolve/import Category B documents through `DocumentAvailabilityScope.User`, returning the canonical stored copy without requiring one physical page per channel.
   - Category B collision handling: fingerprint remains the primary dedupe key to match runtime conventions, but the dry run must detect same-fingerprint/multiple-source cases and planned collisions with existing local slugs. If multiple remote rows would land on the same scoped slug, the import must fail unless the rows are byte-identical and metadata-equivalent, or the approved deterministic merge rule applies (newest `updated_at` wins, losers recorded in the manifest as `collision_strategy=merged`). Fingerprints are never suffixed — that would break content-addressability and `ExistsAsync` dedupe.
   - Remote GUIDs inside Category A keys are preserved verbatim as the key suffix (provenance + collision-free + reversible); no cross-environment GUID remap is needed because the scope wrapper always uses local GUIDs.
   - Delete nothing pre-rewrite; old slugs are simply replaced/repurposed by the UPDATE. No `DELETE` of `doc/%` rows is performed — the base copy UPDATE rewrites the original row in place (page ids, versions, tags, chunks stay attached).
   - After the rewrite, NULL the embedding columns on all restored chunk rows (`embedding`, `embedding_image`, `embedding_multimodal`) because remote vectors were produced by `zeroentropyai:zembed-1` while local queries embed with `openai:embedding` (LiteLLM → Azure `text-embedding-3-large-1`). The 3072 dims match, but zembed-1 and text-embedding-3-large are different vector spaces and gbrain's `searchVector` applies no model guard (`postgres-engine.ts:2126-2280`) — keeping them would silently corrupt retrieval (R5). tsvector columns are rebuilt automatically by the existing triggers.

9. **Re-embed with the local model.** `docker compose exec gbrain gbrain migrate embeddings --to openai:embedding --dim 3072 --yes` (per `gbrain migrate embeddings --help`: handles dimension changes, pages without recorded embedding signature (#3391), query cache, resume-after-kill; never re-embeds an already-migrated chunk twice). Confirm the target resolves through the local LiteLLM `embedding` route (already proven: 8 local chunks are embedded with `openai:embedding`). If the migrate pass cannot process NULL-vector chunks, fall back to `gbrain embed --stale --include-null-signature` or re-put pages via `gbrain put`/MCP (R6).

10. **Supplementary tables — mandatory resolution before gate.** `tags` (1,942) and `page_aliases` (94) must be fully resolved during implementation, not left optional. Import `tags` as-is if FK integrity holds. For `page_aliases`, inspect schema first, rewrite any alias strings that reference old slugs using the same mapping, and verify the aliases resolve after rewrite. If `page_aliases` cannot be mapped safely, exclude them deliberately, document the reason, and update outputs/exit criteria/evidence accordingly before approval. `config` is never imported — the local config (embedding model, `version 125`, `search.mode tokenmax`) must win.

11. **Post-import validation.**
   - Row counts: 3,148 pages imported, 5,261+ chunk rows, per-category prefix counts exactly as mapped; zero rows still matching `learning/facts/%`, `doc/%`, or bare raw slugs (excluding the pre-existing local `memory/...` rows and `__lk_probe_write__`).
   - `gbrain list`, `gbrain get <new-slug>`, `gbrain search <query>` inside the local gbrain container on samples from each category.
   - Gateway smoke test (chat with memory retrieval): the model's memory search resolves a Category A fact and a Category C concept; `document_search` and `document_list` surface Category B documents from every discovered/permitted channel in `CH_ALL` while returning the single canonical user-scoped stored copy.
   - Merge/dedupe validation: the updated document tool/store path returns one row per canonical document; `document_search` keeps the highest-scoring hit per canonical slug/fingerprint and applies deterministic tie-breaks (`IngestedAt` desc, then slug asc), while `document_list` de-duplicates by canonical slug/fingerprint and sorts by `IngestedAt` desc, then slug asc.
   - Collision validation: no unexpected duplicate target slugs, and any approved same-fingerprint duplicates are recorded in the manifest with their resolution.
   - Alias/tag validation: imported `tags` retain FK integrity; `page_aliases` either resolve correctly after rewrite or are explicitly absent with documented rationale.
   - Embedding sanity: no NULL embeddings remain; `gbrain stats`/`search` quality spot check.

12. **Repeat against the real `gbrain` DB** after staging passes, then re-run validation (step 11) and record evidence.

13. **Capture approved-state backup.** After the real import and validation pass, take a full backup of the final imported `gbrain` database to `scripts/gbrain/backups/gbrain-post-import-<timestamp>.sql`. This preserves the approved post-import state so it can be restored without rerunning export, rewrite, or re-embedding.

14. **Code quality gates** (per AGENTS.md) for any repo changes shipped with the script: run tests, coverage ≥ 80%, `scripts/quality/sonarqube-scan.sh` (no Blocker/Critical/Major), deep review sub-agent; update `docs/operations` if the import script is to be a documented runbook.

## Review Focus
- Import is confined to the `gbrain` database/tables; local identity and `config` untouched.
- Credentials never appear in commands, scripts, or logs (files referenced by path only).
- Mapping is lossless and reversible: original remote paths recoverable from new slugs (Category A) or manifest (all categories); rollback = pre-import backup.
- Embedding model mismatch fully resolved — no stale `openai:embedding-small` vectors left locally.
- Imported content is retrievable by the local runtime from every discovered local channel before the gate closes.
