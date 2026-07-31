# GBrain Embeddings: Configuration, Provenance, and Troubleshooting

GBrain (`github:garrytan/gbrain`, a Bun package) embeds page content into vector chunks at write time and compares query embeddings against stored chunk vectors for retrieval. This page documents how the embedding model is selected, how to detect when stored vectors were produced by a different model than the one used for queries, and how to re-embed.

## How the embedding model is resolved

Resolution order (runtime embed path):

1. `GBRAIN_EMBEDDING_MODEL` environment variable — the gateway's `getEmbeddingModel()` reads this (gbrain source `src/core/ai/gateway.ts`); used for every embedding call at runtime.
2. `config.embedding_model` row in the GBrain database — written **once at `gbrain init`** (and by `migrate embeddings` / `ze-switch`); used as a fallback when the env var path is unavailable (`src/core/postgres-engine.ts:2462`).
3. Compile-time `DEFAULT_EMBEDDING_MODEL` — currently `zeroentropyai:zembed-1` — last resort for fresh brains.

Local compose sets `GBRAIN_EMBEDDING_MODEL=openai:embedding` and `GBRAIN_EMBEDDING_DIMENSIONS=3072` (`docker-compose.yml` gbrain service). `config/gbrain/start-gbrain.sh` defaults to `openai:embedding-small` when the env var is unset.

## The provenance trap: `config.embedding_model` goes stale

The `config` table records what `gbrain init` was told — it is **not** kept in sync with later env changes, and it does not record what actually produced existing vectors. The authoritative provenance is the per-chunk `model` column:

```sql
-- In the GBrain database (local: db "gbrain"; swarm: db "leankernel"):
SELECT model, count(*) FROM content_chunks GROUP BY 1;
```

Real-world example (remote swarm, 2026-07-31): the live `.env` set `GBRAIN_EMBEDDING_MODEL=openai:embedding`, `.env.example` still said `openai:embedding-small`, the `config` row said `openai:embedding-small` — but **all 5,261 chunks were stamped `zeroentropyai:zembed-1`**, meaning the vectors were produced at a time when the ZeroEntropy default was in effect. Debugging on `config` alone would have misled.

Checklist when diagnosing embedding issues:

1. `SELECT model, count(*) FROM content_chunks GROUP BY 1;` — what actually embedded the stored vectors.
2. `SELECT key, value FROM config WHERE key LIKE 'embedding%';` — what init was told (may be stale).
3. `docker compose exec gbrain sh -c 'echo $GBRAIN_EMBEDDING_MODEL'` — what the runtime will use for new embeds.
4. Compare all three; they must agree for retrieval to be sound.

## Same dimensions ≠ same vector space

Two embedding models can output the same number of dimensions while living in completely different vector spaces. Cosine similarity between vectors from different models is meaningless, and GBrain's vector search applies **no model or provenance guard**: `searchVector` in `src/core/postgres-engine.ts:2126-2280` runs raw `cc.embedding <=> query` distance over every chunk. Mixing models therefore degrades retrieval **silently** — no error is raised.

- Remote swarm vectors: `zeroentropyai:zembed-1` (ZeroEntropy), 3072 dims.
- Local vectors: `openai:embedding` → LiteLLM `embedding` route → Azure `text-embedding-3-large-1` (order 1), `gemini-embedding-2` fallback (`config/litellm/config.yaml:229-244`), 3072 dims.
- Same 3072 dims, incompatible spaces. Migrating data between brains (or restoring a dump) must re-embed.

Symptom of mixed models: keyword (tsvector) search works, but vector/hybrid search returns nonsense rankings with plausible-looking scores.

## Re-embedding a brain onto the local model

```sh
docker compose exec gbrain gbrain migrate embeddings --to openai:embedding --dim 3072 --yes
```

`gbrain migrate embeddings --help` notes:

- Handles dimension changes (schema transition), pages without a recorded embedding signature (#3391), the query cache, and resume-after-kill.
- Already-migrated chunks are never re-embedded twice — safe to re-run.
- `--dry-run` produces a plan + cost estimate without changing anything.
- `--no-embed` applies schema/config/invalidation but skips the embed pass; follow with `gbrain embed --stale --include-null-signature` (or `--background`).
- `--ignore-env-override` proceeds even when `GBRAIN_EMBEDDING_*` env vars would override the target at runtime.

Related commands: `gbrain ze-switch` manages the ZeroEntropy default switch (with `--dry-run`, `--undo`, `--resume`); `gbrain doctor` reports resolver/pgvector/embeddings health; `gbrain stats` shows brain statistics.

If restoring a dump whose vectors must not survive, NULL the embedding columns first so the migrate pass re-embeds everything:

```sql
UPDATE content_chunks SET embedding = NULL, embedding_image = NULL, embedding_multimodal = NULL;
```

## Where the GBrain database lives

GBrain uses whichever Postgres database `start-gbrain.sh` resolves — `GBRAIN_DB_URL`, else `${POSTGRES_DB:-leankernel}` (`config/gbrain/start-gbrain.sh:4-9`). Two consequences:

- The local compose stack sets `POSTGRES_DB=${GBRAIN_POSTGRES_DB:-gbrain}` for the gbrain service, so local GBrain tables live in the dedicated `gbrain` database.
- The remote swarm (2026-07-31) did **not** set `GBRAIN_DB_URL`/`POSTGRES_DB` for GBrain, so its GBrain tables live **inside the `leankernel` database** — there is no separate `gbrain` database on the swarm host.

`config.version` in the GBrain database is the GBrain schema version (local 125 vs remote 116 on 2026-07-31) — a quick drift indicator when comparing environments.

## Accessing the remote swarm database

- Credentials live in `~/source/repos/swarm/deploy/platform/secrets/` (`postgres_user.txt`, `postgres_password.txt`); reference them by path, never in command lines or logs.
- The local `database` container has `psql`/`pg_dump` and can reach the remote host directly (Postgres 16.14 remote):
  ```sh
  docker cp ~/source/repos/swarm/deploy/platform/secrets/postgres_password.txt leankernel-database:/tmp/
  docker compose exec -T database sh -c 'PGPASSWORD=$(cat /tmp/postgres_password.txt) psql -h 192.168.*.* -U "$(cat /tmp/postgres_user.txt)" -d leankernel -c "..."'
  ```
- tsvector (`search_vector`) is maintained by triggers on `pages`/`content_chunks`; embeddings are not — they are written by the embed path and must be regenerated deliberately (see above).

## Related

- Import plan (2026-07-31): [`docs/plans/2026-07-31-gbrain-import-to-local/index.md`](../plans/2026-07-31-gbrain-import-to-local/index.md) — remote-to-local GBrain data import, slug rewrites, embedding regeneration.
- [`memory-pipeline.md`](../features/memory-pipeline.md) — memory scope and transport conventions.
- [`0004-keep-gbrain-transport-in-gateway.md`](../decisions/0004-keep-gbrain-transport-in-gateway.md) — GBrain transport ownership.
