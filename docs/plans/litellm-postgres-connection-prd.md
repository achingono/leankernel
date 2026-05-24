# PRD: Configure LiteLLM Postgres Connectivity in Compose

## Context
LiteLLM previously failed to initialize with Postgres when the database password contained URL-reserved characters (for example `[` and `]`).

## Goal
Ensure LiteLLM reliably connects to the Compose `database` Postgres service using a safe `DATABASE_URL` at runtime.

## Reviewed Plan
1. Keep LiteLLM startup via `sh -c` and a single command script in `docker-compose.yml`.
2. Compute URL-encoded password from `POSTGRES_PASSWORD` at container startup using Python `urllib.parse.quote`.
3. Construct and export:
   `DATABASE_URL=postgresql://<user>:<encoded-password>@database:5432/<db>`
4. Keep existing config render step and launch LiteLLM after `DATABASE_URL` export.
5. Keep existing LiteLLM healthcheck behavior.
6. Validate with:
   - `docker compose config`
   - `docker compose up -d --build litellm`
   - `docker compose ps litellm`

## Constraints
- Do not print secrets in logs.
- Preserve existing model config rendering and health behavior.

## Acceptance Criteria
- LiteLLM container starts without crash-loop when Postgres password has reserved URL characters.
- LiteLLM reaches healthy state in Compose.
- `DATABASE_URL` is assembled at runtime from standard Postgres env vars.
