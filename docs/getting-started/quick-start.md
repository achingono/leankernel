# Quick Start

This is the fastest way to bring up the current local stack.

## Prerequisites

- .NET 10 SDK
- Docker and Docker Compose
- optional model provider secrets for LiteLLM, depending on the models you enable

## Start the Full Local Stack

From the repository root:

```bash
docker compose up -d --build
```

This starts:

- PostgreSQL with `pgvector`
- LiteLLM
- GBrain
- Playwright run-server
- Webwright MCP server
- LeanKernel Gateway
- Signal services (`signal-cli` and `signal-terminal`)
- Teams terminal (`teams-terminal`)

Reference: [`../../docker-compose.yml`](../../docker-compose.yml)

## Verify Gateway Health

```bash
curl http://127.0.0.1:8080/health
```

Expected result: HTTP 200 with a small JSON payload containing `status: healthy`.

Health endpoint mapping lives in [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs).

## Key Local URLs

- Gateway: `http://127.0.0.1:8080`
- Gateway health: `http://127.0.0.1:8080/health`
- LiteLLM: `http://127.0.0.1:4000`
- GBrain: `http://127.0.0.1:8789`
- Webwright: `http://127.0.0.1:8000`
- Teams terminal health: `http://127.0.0.1:3978/live`

## Next Reads

- [Local development](local-development.md)
- [Gateway API](../api/gateway-api.md)
- [Docker compose stack](../operations/docker-compose-stack.md)
