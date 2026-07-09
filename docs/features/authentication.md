# Authentication and Authorization

This document reflects the current authentication behavior implemented in `LeanKernel.Gateway`.

## Current auth model

Phase 1 currently uses a simple API-key check for the protected Gateway endpoints.

- `GET /api/health` is anonymous so container and orchestration probes can succeed without credentials.
- `POST /api/chat` requires `X-Api-Key` when a gateway key is configured.
- `GET /api/diagnostics/{sessionId}` requires `X-Api-Key` when a gateway key is configured.
- `GET /api/diagnostics/{sessionId}/context`, `/budget`, and `/history` use the same API-key requirement.
- If no gateway key is configured, chat and diagnostics stay open for local development.

## Configuration

Gateway reads these settings:

| Key | Type | Description |
| --- | --- | --- |
| `LeanKernel:Gateway:ApiKey` | string | Single accepted API key. Empty means no auth enforcement. |
| `LeanKernel:Gateway:ApiKeys` | string array | Optional array form for multi-value overrides. Any listed key is accepted. |

## Client behavior

When auth is enabled, clients must send:

```http
X-Api-Key: <configured-key>
```

Example:

```bash
curl http://localhost:5080/api/chat \
  -H "X-Api-Key: replace-me" \
  -H "Content-Type: application/json" \
  -d '{"message":"Hello from LeanKernel"}'
```

## Planned expansion

Broader auth flows such as admin UI auth, bearer-token APIs, and OIDC are still planned rearchitecture work and are not implemented in the current Gateway slice.

## Related documentation

- [Gateway API](gateway-api.md)
- [Context Diagnostics API](context-diagnostics-api.md)
- [Configuration reference](../configuration/configuration-reference.md)
