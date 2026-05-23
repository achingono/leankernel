# Gateway API

`LeanKernel.Gateway` is the ASP.NET Core entry point for the rearchitecture. It composes persistence, knowledge, context, tools, agents, diagnostics, and channel registrations, then exposes both the existing Minimal API surface over `IAgentRuntime` and the foundational Phase 4 Blazor Server chat UI.

## Implemented endpoints

| Endpoint | Method | Auth | Purpose |
| --- | --- | --- | --- |
| `/api/health` | `GET` | Anonymous | Report gateway liveness, ASP.NET health-check status, core service wiring, and tracked provider health. |
| `/api/chat` | `POST` | `X-Api-Key` when configured | Run one chat turn through the composed runtime and return the model response plus session id. |
| `/api/diagnostics/{sessionId}` | `GET` | `X-Api-Key` when configured | Return persisted diagnostic entries for a session. |
| `/api/diagnostics/{sessionId}/context` | `GET` | `X-Api-Key` when configured | Return the stored context admission audit for the latest turn or a specific `turnId`. |
| `/api/diagnostics/{sessionId}/budget` | `GET` | `X-Api-Key` when configured | Return stored budget allocation and usage details for the latest turn or a specific `turnId`. |
| `/api/diagnostics/{sessionId}/history` | `GET` | `X-Api-Key` when configured | Return stored history shaping diagnostics for the latest turn or a specific `turnId`. |

## Chat request contract

`POST /api/chat` accepts:

- `message`: required user input.
- `userId`: optional caller identity. Defaults to `api-user`.
- `channelId`: optional channel identifier. Defaults to `api`.
- `sessionId`: optional existing session identifier. When omitted, Gateway creates one before invoking the runtime.

The response includes:

- `response`: the `IAgentRuntime` string result.
- `sessionId`: the session used for the turn.

## Correlation IDs and rate limiting

Gateway now ensures every request has an `X-Correlation-Id` header. If the caller omits it, Gateway generates one, returns it on the response, and forwards it on outbound `HttpClient` calls made during the request.

Gateway also applies configurable rate limits under `LeanKernel:Hardening:RateLimit` using per-caller minute/hour sliding windows plus concurrent-request caps. Health endpoints are exempt so Docker and orchestration health checks keep working during traffic spikes.

## API-key behavior

Gateway keeps auth intentionally simple for Phase 1:

- If no gateway API key is configured, chat and diagnostics remain open for local development.
- The `/context`, `/budget`, and `/history` diagnostics routes accept an optional `turnId` query parameter and return `404` when no stored snapshot exists for the requested turn.
- If a key is configured, callers must send it in the `X-Api-Key` header.
- The host accepts the single-key appsettings value `LeanKernel:Gateway:ApiKey` and can also read `LeanKernel:Gateway:ApiKeys` for array-based environment overrides.

## Blazor UI coexistence

Gateway now also maps the Blazor Server shell at `/` and `/chat/{sessionId}` after the API routes are registered. The API endpoints above continue to work unchanged because they remain mapped before Razor components.

## OpenAPI

Gateway generates its OpenAPI description from the Minimal API implementation. In Development, the document is exposed at `/openapi/v1.json`.

## Related documentation

- [Diagnostics](diagnostics.md)
- [Context Diagnostics API](context-diagnostics-api.md)
- [Phase 1 Configuration](../configuration/phase-1-config.md)
- [Phase 2 Configuration](../configuration/phase-2-config.md)
- [Channels](channels.md)
- [Solution Structure](../architecture/solution-structure.md)
