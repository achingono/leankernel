# Gateway API

`LeanKernel.Gateway` exposes the primary HTTP endpoints used by UI clients, integrations, and diagnostics tooling.

## Endpoint Summary

| Endpoint | Method | Auth | Description |
| --- | --- | --- | --- |
| `/api/chat` | `POST` | API key required by default | Runs a chat turn through `IAgentRuntime`. |
| `/api/health` | `GET` | Anonymous | Returns service + provider health summary. |
| `/healthz` | `GET` | Anonymous | ASP.NET Core health-check endpoint (`MapHealthChecks`). |
| `/api/diagnostics/{sessionId}` | `GET` | API key required by default | Returns persisted diagnostics entries. |
| `/api/diagnostics/{sessionId}/context` | `GET` | API key required by default | Returns persisted context diagnostics view. |
| `/api/diagnostics/{sessionId}/budget` | `GET` | API key required by default | Returns persisted budget diagnostics view. |
| `/api/diagnostics/{sessionId}/history` | `GET` | API key required by default | Returns persisted history diagnostics view. |
| `/api/admin/ingestion/backfill` | `POST` | Always requires API key | Runs a document-ingestion backfill. |

## Health Endpoints

- `GET /api/health` returns LeanKernel runtime wiring/provider snapshot payload from `Endpoints.cs`.
- `GET /healthz` is the platform health-check route mapped in `Program.cs`.

## `POST /api/chat`

Request shape:

- `message` (required)
- `userId` (required for unauthenticated callers)
- `channelId` (optional)
- `sessionId` (optional)
- `metadata` (optional)

Response shape:

- `response`
- `sessionId`

## Authentication Notes

- Default posture is fail-closed: `LeanKernel:Gateway:RequireApiKey=true` and `LeanKernel:Gateway:AllowAnonymous=false`.
- Local development can opt in to anonymous calls via `LeanKernel:Gateway:AllowAnonymous=true`.
- `/api/admin/ingestion/backfill` always requires a valid `X-Api-Key`, regardless of `AllowAnonymous`.
- Unauthenticated `/api/chat` requests are constrained to the `api` channel namespace.
- A supplied `sessionId` is ownership-validated against the resolved sender for both authenticated and unauthenticated calls.

## Related Pages

- [Diagnostics API](diagnostics-api.md)
- [Configuration reference](../configuration/configuration-reference.md)
- [Operations](../operations/health-and-observability.md)

## Source References

- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/Program.cs`
