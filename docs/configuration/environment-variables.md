# Environment Variables

The most important runtime environment variables are defined by the local Docker Compose stack.

Reference: [`../../docker-compose.yml`](../../docker-compose.yml)

## Gateway

| Variable | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Host environment, currently `Development` in Compose. |
| `ASPNETCORE_HTTP_PORTS` | Gateway listen port inside the container. |
| `CONNECTIONSTRINGS__POSTGRES` | PostgreSQL connection string override for runtime persistence. |
| `OPENAI__BASEURL` | OpenAI-compatible model endpoint, usually LiteLLM. |
| `OPENAI__APIKEY` | API key used by the gateway model client. |
| `OPENAI__DEFAULTMODEL` | Default model id for the main chat path. |
| `OPENAI__MEMORY__MODELID` | Model id for memory-related model work. |
| `OPENAI__FACTEXTRACTION__MODELID` | Model id for fact extraction work. |
| `GBRAIN__BASEURL` | GBrain MCP service base URL. |
| `FILES__ROOTPATH` | Gateway file root used by document ingestion and file-system tools in containers. |
| `FILES__SCRATCHROOT` | Gateway scratch path used for temporary file extraction work in containers. |
| `AGENTS__TOOLS__DOCUMENTINGESTION__ENABLED` | Enables document ingestion hosted services and `document_search`/`document_list` registration. |

## Shared Infrastructure

| Variable | Purpose |
|---|---|
| `POSTGRES_DB` | Shared local database name. |
| `POSTGRES_USER` | Shared database username. |
| `POSTGRES_PASSWORD` | Shared database password. |
| `LITELLM_MASTER_KEY` | LiteLLM API key used by local callers. |
| `LEANKERNEL_GATEWAY_PORT` | Published host port for the gateway. |
| `GBRAIN_PORT` | Published host port for GBrain. |
| `LITELLM_PORT` | Published host port for LiteLLM. |

## GBrain

| Variable | Purpose |
|---|---|
| `GBRAIN_CHAT_MODEL` | Default model for GBrain chat/synthesis operations (e.g. `gbrain think`, subagent dream cycles). Resolves through the gbrain 6-tier model precedence chain: CLI flag → `models.chat` config → `models.default` → `models.tier.reasoning` → this env var → tier default → hardcoded fallback. Default in this stack: `openai:medium`. |
| `GBRAIN_EXPANSION_MODEL` | Default model for GBrain query expansion in hybrid search (used by `gbrain search` / `gbrain query` to expand user queries into multiple sub-queries for better retrieval). Resolves through the same 6-tier precedence chain as `GBRAIN_CHAT_MODEL` but maps to the `utility` tier. Default in this stack: `openai:medium`. |
| `GBRAIN_EMBEDDING_MODEL` | Embedding model used for chunk vectors (e.g. `openai:embedding`). Resolution and staleness caveats: see [`gbrain-embeddings.md`](../operations/gbrain-embeddings.md). |
| `GBRAIN_EMBEDDING_DIMENSIONS` | Embedding vector dimensions (3072 locally). |
| `GBRAIN_DB_URL` | Optional full Postgres URL for GBrain. When unset, `start-gbrain.sh` resolves the database from `POSTGRES_DB` (default `leankernel` — see [`gbrain-embeddings.md`](../operations/gbrain-embeddings.md)). |
| `GBRAIN_POSTGRES_DB` | Compose override for the GBrain database name (default `gbrain`). |
| `GBRAIN_POSTGRES_USER` | Compose override for the database user (default `leankernel`). |
| `GBRAIN_POSTGRES_PASSWORD` | Compose override for the database password (default `leankernel-dev-password`). |
| `GBRAIN_ADMIN_BOOTSTRAP_TOKEN` | **Critical for admin access.** Sets a deterministic bootstrap token for the GBrain admin dashboard at `/admin/#login`. If unset, a random token is generated at startup and hidden from logs (non-TTY guard). Must be set to a known value in any deployment where you need admin dashboard access. See [GBrain Admin Access](../operations/gbrain-admin-access.md). |

## Signal Terminal

| Variable | Purpose |
|---|---|
| `SIGNAL__HOST` | Signal terminal listen address. |
| `SIGNAL__PORT` | Signal terminal listen port. |
| `SIGNAL_CLI_PORT` | `signal-cli` sidecar REST API port. |

## Teams Terminal

| Variable | Purpose |
|---|---|
| `TEAMS_APP_ID` | Bot Framework application (client) ID. |
| `TEAMS_APP_PASSWORD` | Bot Framework client secret. |
| `TEAMS_AUTHORITY` | Microsoft Entra ID authority for Bot Framework auth. |
| `BOT__OPENIDMETADATAURL` | Bot Framework OpenID Connect metadata URL. |
| `BOT__VALIDISSUERS__0` | Allowlisted token issuer for Bot Framework validation. |
| `BOT__ALLOWEDSERVICEURLHOSTSUFFIXES__0` | Allowlisted service URL host suffix for Bot Framework responses. |

## Model Provider Secrets

LiteLLM can also consume provider secrets such as:

- `OPENAI_API_KEY`
- `GROQ_API_KEY`
- `GEMINI_API_KEY`
- `AZURE_AI_API_KEY`
- `GITHUB_COPILOT_OAUTH_TOKEN`

Those are passed through to the LiteLLM container, not directly to the gateway runtime.
