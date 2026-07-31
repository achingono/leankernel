# Phase 2026-07-31 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Remote export artifact | Data-only `pg_dump` of remote GBrain tables (`pages`, `content_chunks`, `page_versions`, `tags`, `page_aliases`) from `192.168.*.*:5432/leankernel` | `scripts/gbrain/backups/remote-gbrain-20260731-103734.sql.gz` (96 MB) |
| Local pre-import backup | Full dump of the local `gbrain` database taken before restore (rollback path) | `scripts/gbrain/backups/gbrain-pre-import-20260731-103734.sql.gz` |
| Local post-import backup | Full dump of the final imported `gbrain` database taken after validation (approved-state restore point) | `scripts/gbrain/backups/gbrain-post-import-20260731-reembed.sql.gz` (48 MB, embeddings included) |
| Import + rewrite script | Reusable, dry-run-capable, staging-tested script implementing the import flow (export, restore, sequence fixup, slug rewrite, embedding regen, supplementary-table handling) and resolving the target identity/channel inputs from the environment | `scripts/gbrain/import-remote.sh` (+ SQL templates in `scripts/gbrain/sql/`, incl. `restore-real.sql` for the id-offset restore path) |
| Document retrieval update | Code/tests updating document search/list behavior to retrieve canonical user-scoped documents from all permitted channels and merge/de-duplicate results deterministically | **Done (2026-07-31):** `GBrainDocumentStoreClient` (store-only change; tools unchanged); 13 unit tests in `GBrainDocumentStoreClientTests` |
| Slug mapping manifest | `old_slug → new_slug` CSV for every rewritten page, with category, canonical-storage flags, and collision strategy/resolution columns | `scripts/gbrain/backups/manifests/rewrite-manifest-real-20260731-103734.csv` (staging: `rewrite-manifest-staging-20260731-102829.csv`) |
| Staging verification notes | Result of running the full flow against `gbrain_import_staging` first (counts, re-embed success, document fan-out behavior, alias/tag handling, channel discovery) | Markdown notes in `evidence.md` |
| Validation evidence | Post-import counts, `gbrain get/search` samples, gateway memory + `document_search`/`document_list` smoke tests from every discovered local channel, embedding NULL check, alias/tag verification | Log snippets in `evidence.md` |

## Optional Outputs
- A short operations runbook entry documenting the script usage (only if the import is expected to recur).

## Output Quality Checklist
- [x] All mandatory outputs produced (backups, manifests, script + SQL templates, doc updates)
- [x] All outputs reviewed before gate
- [x] Evidence log updated with output references
- [x] No credential material present in any committed script or log (secrets read by path; container copies removed on exit)
