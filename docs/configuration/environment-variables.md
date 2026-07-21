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
