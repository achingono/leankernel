# Docker Compose Stack

The local stack is defined in [`../../docker-compose.yml`](../../docker-compose.yml).

## Services

| Service | Purpose |
|---|---|
| `database` | PostgreSQL with `pgvector` |
| `litellm` | OpenAI-compatible model proxy |
| `gbrain` | Wiki and memory MCP service |
| `gateway` | LeanKernel runtime host |

## Startup Ordering

The gateway depends on health checks for:

- database
- LiteLLM
- GBrain

This means `docker compose up` is intended to bring up a fully connected runtime, not just the web host.

## Runtime Overrides Applied by Compose

- gateway uses PostgreSQL via `ConnectionStrings__Postgres`
- gateway points `OpenAI__BaseUrl` at LiteLLM
- gateway points `GBrain__BaseUrl` at the GBrain service

## Health Checks

- database: `pg_isready`
- LiteLLM: `/health/liveliness`
- GBrain: `/health`
- gateway: `/health`
