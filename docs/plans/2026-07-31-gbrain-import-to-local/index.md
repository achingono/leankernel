# Phase 2026-07-31 GBrain Import To Local

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Import the remote swarm GBrain knowledge/memory data (Postgres at `192.168.*.*:5432`) into the local Docker Compose stack's `gbrain` database, rewriting every page/document slug into the current runtime scope convention — `memory/{tenantId}/{personId}/{channelId}/{key}` and `documents/{tenantId}/{scope}/{channelId}/{userId}/{fingerprint}` — using an explicit local import-target identity mapping so the imported content falls in the intended local scope and is retrievable from every configured local channel.

The remote data currently uses three legacy slug shapes that the local runtime cannot see:
- `learning/facts/{remoteTenant}/{remotePerson}/{seq}` (1,434 pages) — not under the `memory/` prefix
- `doc/{title-slug}` (1,589 pages) — no identity scope at all
- raw slugs such as `what/budget`, `how/network`, `identity-user-default` (125 pages)

All must be wrapped into the current convention (see `activities.md` mapping table).

## Scope

## In Scope
- Read remote DB credentials from `~/source/repos/swarm/deploy/platform/secrets/` without ever printing them (verified present: `postgres_user.txt`, `postgres_password.txt`).
- Export the GBrain tables only from the remote `leankernel` database (the swarm runs GBrain inside it; there is no separate `gbrain` database remotely).
- Import into the local `gbrain` database (local stack: `pgvector/pgvector:pg16`, DB `gbrain`, user `leankernel`).
- Rewrite all imported slugs to the current runtime convention with an explicit local import-target identity mapping (tenant/person/user + discovered local channels); memory remains channel-scoped, while imported documents are normalized into canonical user-scoped namespaces and retrieved from all configured local channels via memory-policy fan-out (`MemoryPolicyDefaults` = `["*"]`) plus channel-authorized document retrieval.
- Regenerate embeddings locally: remote vectors were produced by `zeroentropyai:zembed-1` while local queries embed with `openai:embedding` (Azure `text-embedding-3-large-1`). The dimensions match (3072) but the vector spaces differ, and GBrain's vector search applies no model guard — so remote vectors must be re-embedded after import.
- Post-import validation against the local GBrain runtime and the gateway memory/document tools.
- Ship a reusable, dry-run-capable, staging-tested import script under `scripts/gbrain/`.

## Out of Scope
- Rebuilding or changing the GBrain application (external package `github:garrytan/gbrain`).
- Importing the remote LeanKernel app data (the remote `leankernel` DB has no identity tables; identity lives elsewhere in the swarm).
- Importing GBrain operational/telemetry tables (`query_cache`, `search_telemetry`, `ingest_log`, `mcp_*_log`, `extract_rollup_7d`, `access_tokens`, `oauth_*`, `budget_*`, `minion_*`, `subagent_*`, `take_*`, `eval_*`, `sources`, `raw_data`, `config`, `gbrain_cycle_locks`, `op_checkpoints*`, `drift_decisions`, `dream_verdicts`, `conversation_parser_llm_cache`, `code_*`, `calibration_profiles`, `context_volunteer_events`, `file_migration_ledger`, `synthesis_evidence`, `timeline_entries`, `think_ab_results`).
- Changing the runtime authorization model beyond what the path rewrite requires.

## Entry Criteria
- Local Docker Compose stack running with `database` and `gbrain` services healthy (verified 2026-07-31).
- Swarm secrets folder present with working remote credentials (verified: connectivity OK on `192.168.*.*:5432`; credentials read without display).
- Remote inventory complete: `pages` 3,148 (deleted_at null), `content_chunks` 5,261, `page_versions` 1,449, `tags` 1,942, `page_aliases` 94, `links`/`files`/`facts` 0 (verified 2026-07-31).
- Local and remote `pages`/`content_chunks` schemas identical (column sets verified; only ordinal order differs).
- Plan reviewed by a separate model/session before implementation.

## Exit Criteria
All checks in [exit-criteria.md](exit-criteria.md) are complete.

## Roles
- Owner: Coding agent
- Reviewer: Separate model/session reviewer
- Approver: Repository maintainer
