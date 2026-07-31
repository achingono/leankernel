# Phase 2026-07-31 Exit Criteria

## Gate Checklist
- [x] Plan reviewed by a separate model/session before implementation.
- [x] Pre-import backup of the local `gbrain` database exists and restores cleanly (`backups/gbrain-pre-import-20260731-103734.sql.gz`).
- [x] Post-import backup of the approved final `gbrain` database exists and restores cleanly (`backups/gbrain-post-import-20260731-reembed.sql.gz`).
- [x] Remote export is data-only, limited to the GBrain tables, and does not include unrelated services/databases.
- [x] Full flow verified against a staging database before the real import (dry-run + apply + re-embed + validation all green).
- [x] Local `gbrain` DB contains all 3,148 remote pages rewritten into canonical local scopes without per-channel document duplication (286 unique documents after merge), 5,269 chunk rows, 1,449 page versions, and fixed sequence values.
- [x] Zero slugs remain in legacy shapes (`learning/facts/%`, `doc/%`, bare raw slugs); every imported page matches `memory/{LT}/{LP}/{channel}/...` or `documents/{LT}/user/{Guid.Empty}/{LU}/{fp}` using the resolved import-target identity mapping (validation: 0/0/0).
- [x] Mapping manifest CSV produced and spot-checked (`backups/manifests/rewrite-manifest-real-20260731-103734.csv`; staging manifest `rewrite-manifest-staging-20260731-102829.csv`).
- [x] All chunk embeddings regenerated with `openai:embedding`; no NULL embeddings and no `openai:embedding-small` vectors remain (2,993 embedded, null 0; remote provider was `zeroentropyai:zembed-1`).
- [x] Local runtime retrieves imported content: `gbrain get/search` succeeds on samples (`gbrain get` 01-mission winner slug; `gbrain search "leadership"` returns canonical documents slugs); **gateway memory + `document_search`/`document_list` smoke tests pass** (identity, memory Cat A/C, document_list 100 docs, document_search "mission" → 01-mission; no memory leaks).
- [x] Multi-hit document retrieval is merged/de-duplicated deterministically: `document_search` groups hits by fingerprint (last slug segment), keeps the highest score per document with the lexical slug ascending as the deterministic tie-break (search payloads carry no timestamps, so `IngestedAt` is not usable for ordering); `document_list` de-duplicates by fingerprint and orders by newest `updated_at` (real timestamp from `list_pages`) then slug asc. Verified against the deployed gbrain 0.42.67.0 MCP surface.
- [x] Local `config` (embedding model, version 125, search mode) unchanged; local identity tables untouched; target identity/channel values used by the import are recorded as run inputs (tenant `e44ca455-…`, person `7d1282a7-…`, user `0a6e0986-…`, channel `9ff45fac-…`).
- [x] No credentials appear in terminal output, scripts, logs, or the manifest (read by path only; container copies removed on exit).
- [x] `tags` (1,942) and `page_aliases` (94) imported and validated (alias/tag orphans 0).
- [x] Repo quality gates pass for any committed changes (tests, coverage ≥ 80%, SonarQube scan clean of Blocker/Critical/Major, deep review).

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
