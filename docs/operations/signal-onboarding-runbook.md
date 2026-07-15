# Signal Onboarding Runbook

Runbook for registering a Signal account in the local compose stack and validating end-to-end terminal behavior.

## Preconditions

- `docker compose up -d` stack is running, including `signal-cli`, `signal-terminal`, `gateway`, and `database`.
- Signal sidecar is healthy:
  - `docker compose ps signal-cli`
- Signal API is reachable from host:
  - `curl -sS http://localhost:${SIGNAL_CLI_PORT:-8081}/v1/about`

## 1) Register the Signal account

Use E.164 format (example: `+12262274494`).

- Initial registration:
  - `docker compose exec signal-cli sh -lc "curl -sS -X POST -H 'Content-Type: application/json' 'http://localhost:8080/v1/register/+12262274494'"`
- If captcha is required, generate token from <https://signalcaptchas.org/registration/generate.html> and retry:
  - `docker compose exec signal-cli sh -lc "curl -sS -X POST -H 'Content-Type: application/json' -d '{\"captcha\":\"<captcha-token>\"}' 'http://localhost:8080/v1/register/+12262274494'"`
- Verify with SMS/voice code:
  - `docker compose exec signal-cli sh -lc "curl -sS -X POST -H 'Content-Type: application/json' 'http://localhost:8080/v1/register/+12262274494/verify/<code>'"`

Notes:

- A repeated register attempt may return rate-limited (`429`) after a successful `201`; this does not mean verification failed.
- Account availability may require restarting `signal-cli` once after verification:
  - `docker compose restart signal-cli`

## 2) Confirm account discovery

- List accounts:
  - `docker compose exec signal-cli sh -lc "curl -sS 'http://localhost:8080/v1/accounts'"`

Expected: the registered number is present (for example `"+12262274494"`).

## 2.5) Set Signal profile metadata

Some Signal registrations remain constrained until profile metadata is set.

- Set profile display name:
  - `docker compose exec signal-cli sh -lc "curl -sS -w '\nHTTP %{http_code}\n' -X PUT -H 'Content-Type: application/json' -d '{\"name\":\"LeanKernel Bot\"}' 'http://localhost:8080/v1/profiles/+12262274494'"`
- Optionally set avatar (base64 image):
  - `AVATAR_BASE64=$(base64 -i ./docs/assets/brand/logo-mark.png | tr -d '\n') && curl -sS -w '\nHTTP %{http_code}\n' -X PUT -H 'Content-Type: application/json' -d '{"name":"LeanKernel Bot","base64_avatar":"'"$AVATAR_BASE64"'"}' "http://localhost:${SIGNAL_CLI_PORT:-8081}/v1/profiles/+12262274494"`

Expected: profile update returns `HTTP 204`.

## 3) Provision sender bindings

Terminal forwarding is fail-closed. A sender must match an active row in `ChannelSenderBindings` for channel `signal` with issuer `signal`, and with non-empty `BearerToken`.

- Inspect existing Signal bindings:
  - `docker compose exec database sh -lc "psql -U leankernel -d leankernel -c \"select c.\\\"Name\\\" as channel, b.\\\"Issuer\\\", b.\\\"Subject\\\", b.\\\"IsActive\\\", (b.\\\"BearerToken\\\" <> '') as has_token from \\\"ChannelSenderBindings\\\" b join \\\"Channels\\\" c on c.\\\"Id\\\"=b.\\\"ChannelId\\\" where c.\\\"Name\\\"='signal';\""`

Implementation notes:

- `BearerToken` must be JWT-shaped (`header.payload.signature`); malformed values are rejected by gateway bearer auth.
- Channel token used by the terminal must include channel claims expected by gateway middleware: `lk_tenant_id` and `lk_channel`.
- Keep sender identity aligned across binding and token claims: `ChannelSenderBindings.UserId` should reference a user whose `(Issuer, Subject)` matches the token sender identity.

## 4) Validate message flow

- Send a test message from the bound sender to the registered Signal number.
- Tail logs:
  - `docker compose logs signal-cli --since=2m`
  - `docker compose logs signal-terminal --since=2m`
  - `docker compose logs gateway --since=2m`

Expected flow:

- `signal-cli` shows `GET /v1/receive/<account>?timeout=20` returning `200` with long-lived receive windows.
- `signal-terminal` resolves sender token and posts to gateway.
- `gateway` shows `POST /v1/responses` request handling and successful response path.

## Troubleshooting

- **No response in Signal, sender rejected in terminal**
  - Check `signal-terminal` logs for: `Rejecting Signal sender ... no binding token configured.`
  - Add or activate the binding for the exact sender subject observed in logs.

- **Gateway auth failure (`IDX14100`)**
  - Cause: non-JWT `BearerToken` in `ChannelSenderBindings`.
  - Fix: update binding token to valid JWT-shaped value.

- **Gateway returns `401` without `IDX14100`**
  - Cause: channel claims missing from bearer token (`lk_tenant_id`, `lk_channel`) or binding/user/token identity mismatch.
  - Fix: regenerate token with required channel claims and align `ChannelSenderBindings.UserId` with token issuer/subject identity.

- **Terminal logs `Signal message rejected ... no 'dataMessage' or 'syncMessage.sentMessage' in envelope`**
  - Cause: non-text Signal envelope (typing/receipt/update event).
  - Fix: expected behavior; ignore unless text messages are also rejected.

- **Gateway returns `missing_required_parameter`**
  - Cause: terminal request missing `agent.name` for `/v1/responses`.
  - Fix: ensure terminal payload includes `agent.name` alongside `model` and `input`.

- **Signal account not discovered**
  - Confirm `/v1/accounts` contains the number.
  - Restart `signal-cli` and `signal-terminal` if registration was just completed.

## Related pages

- [Docker compose stack](docker-compose-stack.md)
- [Channels feature details](../features/channels.md)
