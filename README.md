# LeanKernel

LeanKernel is a personal AI agent platform built on **.NET 10**. It combines:

- A single turn runtime (`IAgentRuntime`/`ThinkerService`)
- Context-gated memory (`Archivist`) with wiki + vector retrieval
- Channel adapters and a durable outbound queue (`Commander`)
- Runtime tools and hot-reload skills (`Plugins`)
- Scheduled maintenance/proactive jobs (`Scheduler`)
- A Blazor admin/chat UI and REST/OpenAI-compatible APIs (`Host`)

## Solution Layout

| Project | Responsibility |
| --- | --- |
| `LeanKernel.Core` | Shared config, contracts, enums, models |
| `LeanKernel.Commander` | Channel adapters, routing, durable outbound queue |
| `LeanKernel.Thinker` | Turn orchestration, model strategies, routing, post-turn pipeline |
| `LeanKernel.Archivist` | Sessions, wiki memory, context gating, embeddings, knowledge search |
| `LeanKernel.Scheduler` | Cron scheduler and proactive jobs |
| `LeanKernel.Plugins` | Built-in tools, attachments, runtime skill loading |
| `LeanKernel.Generators` | Tool registry source generation |
| `LeanKernel.Host` | ASP.NET Core APIs, Blazor UI, auth, onboarding, composition |

## Runtime Topology (Docker Compose)

Default `docker-compose.yml` services:

- `engine` (`leankernel-engine`) - .NET host + UI/API
- `litellm` - model proxy with proxy-layer route logging and in-container model-limit sync
- `qdrant` - vector store
- `unstructured` - document parsing API
- `indexer` - sidecar index pipeline
- `signal` - signal-cli HTTP sidecar

## Quick Start

```bash
cp .env.example .env
# edit provider keys as needed

docker compose up -d
docker compose logs -f engine
```

UI/API default URL: `http://localhost:5080` (or `LEANKERNEL_PORT`).

## Local Development

```bash
cd src
dotnet restore LeanKernel.sln
dotnet build LeanKernel.sln --no-restore -v minimal
dotnet test LeanKernel.sln --no-build -v minimal
dotnet run --project LeanKernel.Host
```

## Authentication

Supported auth modes (`LeanKernel:Auth:Mode`):

- `LocalPasscode` (default)
- `Oidc`
- `Disabled` (development only; non-development falls back to `LocalPasscode`)

Local passcode storage uses **PBKDF2-SHA512** (`200,000` iterations) with per-secret salt and fixed-time verification. API tokens are prefixed with `sk-LeanKernel-`, stored as SHA-256 hashes, and managed via `/api/auth/tokens`.

## APIs

### Internal API (selected)

- Health: `GET /api/health`
- Auth: `/api/auth/*`
- Onboarding: `/api/onboarding/*`
- Chat/session admin: `/api/chat/*`
- Wiki: `/api/wiki/*`
- Config: `GET/PATCH /api/config`
- Routing config read: `GET /api/routing-config`, `GET /api/routing-config/raw`
- Model drift: `GET /api/model-limit-drift`
- Logs/files/stats: `/api/logs`, `/api/files/*`, `/api/stats`

### OpenAI-compatible API

- `POST /v1/chat/completions`
- `GET /v1/models`

These endpoints require bearer auth (`ApiAccess` policy).

## Built-in Tools

Built-in tool names registered at startup:

> **Note:** The tool previously named `wiki_query` is now `search_wiki` for consistency. Update any skill definitions or API consumers accordingly.

- `search_wiki`, `search_documents`, `search_knowledge`
- `web_search` (DuckDuckGo Instant Answer API)
- `file_read`, `file_write`, `file_edit`, `file_search`
- `file_delete`, `file_move`, `file_copy`, `file_chmod`, `file_stat`, `file_touch`
- `directory_list`, `directory_mkdir`

Runtime skills are loaded from `LeanKernel:Skills:BasePaths` (default `/app/data/skills`) and hot-reloaded from `SKILL.md` changes.

## Quality Commands

```bash
# Coverage gate (default threshold: 80)
scripts/quality/test-coverage.sh

# Local Docker-backed SonarQube scan
scripts/quality/sonarqube-scan.sh
```

## Documentation

- `docs/index.md` - documentation entrypoint
- `docs/architecture/` - architecture and flow references
- `docs/features/` - auth and routing implementation details
- `docs/skills/` - runtime skill system
- `docs/development/` - quality gates and LiteLLM config compiler details
- `docs/plans/` - forward-looking roadmap PRDs

## License

Private — All rights reserved.
