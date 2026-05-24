# PRD: Configure GBrain for Postgres + LiteLLM Embeddings

## Context
GBrain is currently started with local PGLite and embeddings disabled (`gbrain init --pglite --no-embedding`).
Target behavior is to run GBrain against the Compose `database` (Postgres) service and use embeddings served through LiteLLM.

## Goal
1. GBrain initializes against Postgres in Compose.
2. GBrain uses LiteLLM as OpenAI-compatible embedding provider.
3. Startup remains idempotent and service health checks continue to pass.

## Reviewed Implementation Plan
1. Update `gbrain` service environment in `docker-compose.yml`:
- Add Postgres connection inputs (`POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`).
- Add LiteLLM/OpenAI-compatible embedding inputs (`OPENAI_BASE_URL`, `OPENAI_API_KEY`).
- Add optional overrides: `GBRAIN_DB_URL` and `GBRAIN_EMBEDDING_MODEL`.

2. Replace hardcoded GBrain init command with runtime command that:
- URL-encodes DB password safely.
- Constructs DB URL from env when `GBRAIN_DB_URL` is not provided.
- Runs `gbrain init --url <db-url> --embedding-model <model>`.
- Starts `gbrain serve --http --port 8789`.

3. Ensure service readiness:
- Keep/ensure `depends_on` for GBrain on `database` and `litellm` health.
- Keep GBrain healthcheck endpoint validation.

4. Update `.env` defaults:
- Add explicit knobs for `GBRAIN_DB_URL` and `GBRAIN_EMBEDDING_MODEL`.

5. Validate:
- `docker compose up -d --build`
- `docker compose ps`
- GBrain health endpoint returns OK.

## Review Notes Applied
- Explicit precedence: `GBRAIN_DB_URL` (if set) overrides generated URL.
- Runtime URL-encoding for DB password avoids reserved-character breakage.
- Do not commit secrets; only variable plumbing/default placeholders are changed.

## Acceptance Criteria
- `gbrain` no longer initializes with `--pglite --no-embedding`.
- `gbrain` initializes with `--url` and `--embedding-model`.
- Compose stack starts with `database`, `litellm`, `gbrain`, and `engine` healthy.
