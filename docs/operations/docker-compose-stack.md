# Docker Compose Stack

The local stack is defined in [`../../docker-compose.yml`](../../docker-compose.yml).

## Services

| Service | Purpose |
|---|---|
| `database` | PostgreSQL with `pgvector` |
| `litellm` | OpenAI-compatible model proxy |
| `gbrain` | Wiki and memory MCP service |
| `gateway` | LeanKernel runtime host |
| `signal-cli` | Signal daemon sidecar (`signal-cli-rest-api`, `MODE=json-rpc`) |
| `signal-terminal` | Signal channel terminal process |
| `teams-terminal` | Teams Bot Framework terminal process |

## Startup Ordering

The gateway depends on health checks for:

- database
- LiteLLM
- GBrain

The channel terminals depend on the database and a healthy gateway:

- signal-terminal -> database + gateway + signal-cli
- teams-terminal -> database + gateway

Signal-specific compose wiring:

- `signal-cli` persists account state in the `signal-data` named volume mounted at `/home/.local/share/signal-cli`.
- `signal-terminal` points to `signal-cli` over the compose network by default (`SIGNAL__HOST=signal-cli`, `SIGNAL__PORT=8080`) and discovers accounts from `GET /v1/accounts`.
- `signal-cli` host exposure is available via `${SIGNAL_CLI_PORT:-8081}` for bootstrap operations.

## Signal account bootstrap

- Register with captcha when required:
  - `docker compose exec signal-cli sh -lc "curl -sS -X POST -H 'Content-Type: application/json' -d '{\"captcha\":\"<captcha-token>\"}' 'http://localhost:8080/v1/register/+12262274494'"`
- Verify with SMS/voice code:
  - `docker compose exec signal-cli sh -lc "curl -sS -X POST -H 'Content-Type: application/json' 'http://localhost:8080/v1/register/+12262274494/verify/<code>'"`
- Confirm registration:
  - `docker compose exec signal-cli sh -lc "curl -sS 'http://localhost:8080/v1/accounts'"`
- Set profile display name (recommended immediately after verify):
  - `docker compose exec signal-cli sh -lc "curl -sS -w '\nHTTP %{http_code}\n' -X PUT -H 'Content-Type: application/json' -d '{\"name\":\"LeanKernel Bot\"}' 'http://localhost:8080/v1/profiles/+12262274494'"`

Token provisioning wiring:

- `signal-terminal` and `teams-terminal` both read sender bearer tokens from PostgreSQL (`ChannelSenderBindings.BearerToken`) using `ConnectionStrings__Postgres`.
- Compose sets terminal DB connection strings to the same database service used by gateway.
- Terminal-side token lookup is fail-closed: missing or inactive binding means no forward to gateway.

This means `docker compose up` is intended to bring up a fully connected runtime, not just the web host.

## Runtime Overrides Applied by Compose

- gateway uses PostgreSQL via `ConnectionStrings__Postgres`
- gateway points `OpenAI__BaseUrl` at LiteLLM
- gateway points `GBrain__BaseUrl` at the GBrain service
- gbrain points `OPENAI_BASE_URL` at LiteLLM (`/v1`) and initializes Dream defaults to LiteLLM-compatible models (`GBRAIN_DREAM_REASONING_MODEL`, `GBRAIN_DREAM_UTILITY_MODEL`)
- gateway sets `Files__RootPath=/app/data` and `Files__ScratchRoot=/app/scratch`
- gateway enables document ingestion tools by default via `Agents__Tools__DocumentIngestion__Enabled`

Dream verification after startup:

- `docker compose exec gbrain gbrain models --json`
- `docker compose exec gbrain gbrain dream --phase synthesize --dry-run --json`

## Gateway File Persistence

- `gateway-data` named volume is mounted at `/app/data` so staged and ingested files survive gateway container restarts.
- `gateway-scratch` named volume is mounted at `/app/scratch` for temporary extraction work.
- host watch-folder input is mounted at `./data/watch -> /app/watch` for optional `Files:WatchFolders` mappings.

## Health Checks

- database: `pg_isready`
- LiteLLM: `/health/liveliness`
- GBrain: `/health`
- gateway: `/health`
- teams-terminal: `/health`

## Channel Terminal Environment

- Signal:
  - `SIGNAL_SOCKET_HOST` (defaults to `signal-cli`)
  - `SIGNAL_SOCKET_PORT` (defaults to `8080`)
  - `SIGNAL_CLI_PORT` (host-published sidecar port; defaults to `8081`)
  - `CONNECTIONSTRINGS__POSTGRES` (used to resolve sender->JWT token mappings from `ChannelSenderBindings`)
- Teams:
  - `TEAMS_APP_ID`
  - `TEAMS_APP_PASSWORD`
  - `TEAMS_AUTHORITY` (defaults to `https://login.microsoftonline.com`)
  - Optional bot validation overrides via ASP.NET config binding (`BOT__OPENIDMETADATAURL`, `BOT__VALIDISSUERS__0`, ...)
  - Optional trusted reply-host overrides via `BOT__ALLOWEDSERVICEURLHOSTSUFFIXES__0`, ...
  - `CONNECTIONSTRINGS__POSTGRES` (used to resolve sender->JWT token mappings from `ChannelSenderBindings`)

## Signal troubleshooting

- If Signal receives but gateway is not called, inspect `signal-terminal` logs for parser rejection messages.
- If terminal logs `no 'dataMessage' or 'syncMessage.sentMessage'`, the envelope is likely a non-text event (typing/receipt/update) and can be ignored.
- If gateway logs `IDX14100: JWT is not well formed`, the stored `ChannelSenderBindings.BearerToken` is not JWT-shaped.
- If gateway returns `401` without `IDX14100`, verify terminal bearer token includes `lk_tenant_id` and `lk_channel`, and ensure binding `UserId` matches token sender issuer/subject identity.
- If gateway returns `missing_required_parameter` for `/v1/responses`, ensure terminal payload includes `agent.name`.

## Teams troubleshooting

- If `/api/messages` returns `401`/`403`, verify the Bot Framework token is valid for configured `TEAMS_APP_ID` and issuer set.
- If terminal logs `service URL is not trusted`, verify incoming `serviceUrl` is HTTPS and host suffix is in `Bot:AllowedServiceUrlHostSuffixes`.
- If inbound request is accepted but not forwarded, verify `ChannelSenderBindings` row is active and matches channel `teams`, issuer `teams`, and sender subject.
