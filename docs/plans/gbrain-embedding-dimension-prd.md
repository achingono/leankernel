# GBrain Embedding Dimension Alignment PRD

## Overview

GBrain page writes currently fail because the configured embedding route returns 3072-dimensional vectors while the existing Postgres-backed GBrain schema expects 1536-dimensional vectors. LeanKernel's active knowledge path delegates embedding generation and vector persistence to GBrain through MCP `put_page`; the legacy Python indexer is not part of the current architecture.

## Problem statement

Post-turn learning is extracting facts from chat sessions, but GBrain rejects persistence with an embedding dimension mismatch:

```text
Embedding dim mismatch: model embedding-small returned 3072 but schema expects 1536.
```

Because writes fail, fact pages never reach `public.pages`, and the knowledge page appears empty.

## Goals

- Configure GBrain to initialize and run with 3072-dimensional embeddings by default.
- Reinitialize the current empty local GBrain schema to `vector(3072)`.
- Keep model and dimension configuration explicit in Docker Compose and environment examples.
- Document that blind vector trimming is not safe as default persistence behavior.

## Non-goals

- Do not modify the legacy Python indexer; it is not used by this architecture.
- Do not add .NET-side vector trimming for GBrain writes; LeanKernel sends page content to GBrain and does not own the embedding vector at that boundary.
- Do not silently truncate vectors by default. Truncation changes semantic geometry and can reduce retrieval quality unless every read/write path consistently applies the same policy.

## Requirements

### Functional requirements

1. `docker-compose.yml` must pass `GBRAIN_EMBEDDING_DIMENSIONS`, defaulting to `3072`, to the GBrain container.
2. `config/gbrain/start-gbrain.sh` must pass `--embedding-dimensions` to `gbrain init`.
3. `.env.example` must expose `GBRAIN_EMBEDDING_MODEL` and `GBRAIN_EMBEDDING_DIMENSIONS`.
4. Documentation must describe GBrain as Postgres-backed in the current stack and note the embedding dimension pairing.
5. The running empty local schema must be migrated/reinitialized to `vector(3072)` and `embedding_dimensions=3072`.

### Safety requirements

1. Blind trimming is not safe as a default because it discards signal and can make query embeddings inconsistent with stored embeddings.
2. Trimming should only be considered as an explicit compatibility escape hatch in the component that owns embedding generation, and only when all query/write paths apply the same dimensional transform.
3. For the current GBrain MCP path, dimension alignment is the safe fix because GBrain owns embedding generation and persistence.

## Architecture

Active knowledge writes flow through:

```text
LeanKernel learning/UI/tools -> IKnowledgeService -> GBrain MCP put_page -> GBrain embedding + Postgres pgvector storage
```

The Python indexer/Qdrant path is legacy and not used by this architecture.

## Rollout plan

1. Update repository configuration and documentation.
2. Apply the local Postgres schema change while the brain is empty:
   - drop the existing `idx_chunks_embedding` index;
   - alter `content_chunks.embedding` to `vector(3072)`;
   - set `public.config.embedding_dimensions` to `3072`.
3. Rebuild/restart GBrain so `gbrain init` writes the same model/dimension pair.
4. Smoke test a GBrain `put_page`/`get_page` flow through the engine-backed knowledge path.

## Acceptance criteria

- GBrain config reports `embedding_dimensions=3072`.
- `public.content_chunks.embedding` reports `vector(3072)`.
- GBrain doctor no longer reports dimension mismatch.
- A test knowledge page can be written and read successfully.
- LeanKernel build and tests pass.
