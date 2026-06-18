# Gateway API

`LeanKernel.Gateway` exposes the primary HTTP endpoints used by UI clients, integrations, and diagnostics tooling.

## Endpoint Summary

| Endpoint | Method | Auth | Description |
| --- | --- | --- | --- |
| `/api/chat` | `POST` | `X-Api-Key` only when configured | Runs a chat turn through `IAgentRuntime`. |
| `/api/health` | `GET` | Anonymous | Returns service + provider health summary. |
| `/api/diagnostics/{sessionId}` | `GET` | `X-Api-Key` only when configured | Returns persisted diagnostics entries. |
| `/api/diagnostics/{sessionId}/context` | `GET` | `X-Api-Key` only when configured | Returns persisted context diagnostics view. |
| `/api/diagnostics/{sessionId}/budget` | `GET` | `X-Api-Key` only when configured | Returns persisted budget diagnostics view. |
| `/api/diagnostics/{sessionId}/history` | `GET` | `X-Api-Key` only when configured | Returns persisted history diagnostics view. |

## `POST /api/chat`

Request shape:

- `message` (required)
- `userId` (optional)
- `channelId` (optional)
- `sessionId` (optional)
- `metadata` (optional)

Response shape:

- `response`
- `sessionId`

## Authentication Notes

- If `LeanKernel:Gateway:ApiKey` and `LeanKernel:Gateway:ApiKeys` are both empty, API key auth is disabled.
- When configured, callers must include `X-Api-Key`.

## Related Pages

- [Diagnostics API](diagnostics-api.md)
- [Configuration reference](../configuration/configuration-reference.md)
- [Operations](../operations/health-and-observability.md)

## Source References

- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/Program.cs`
