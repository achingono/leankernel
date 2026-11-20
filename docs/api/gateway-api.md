# Gateway API

The current HTTP surface is hosted by `LeanKernel.Gateway`.

## Mapped Endpoints

The composition root currently maps:

- `MapOpenAIResponses()`
- `MapOpenAIConversations()`
- `GET /v1/models`
- `POST /v1/chat/completions`
- `MapProxiedOpenAIChatCompletions()` at `/v1/internal/completions`
- `POST /api/documents/upload`
- `GET /health`
- `MapDevUI()` in Development only

Reference: [`../../src/Services/LeanKernel.Services.Gateway/Program.cs`](../../src/Services/LeanKernel.Services.Gateway/Program.cs)

## Health Endpoint

`GET /health`

Returns a small JSON payload with runtime status and a timestamp.

## OpenAI-Compatible Endpoints

The exact route set for responses and conversations is owned by the MAF hosting extensions, not handwritten controller code.

Current expectation:

- `/v1/models`
- `/v1/responses`
- `/v1/conversations`
- `/v1/chat/completions`

`/v1/chat/completions` and the internal `/v1/internal/completions` path are POST endpoints.

HTTP JSON parsing accepts polymorphic metadata such as `type` or `role` in any
property order. Chat tool definitions must still have a supported string `type`;
malformed definitions receive an OpenAI-shaped 400 error.

The chat-completions proxy forwards to the internal path on the local listener. It preserves
the request host for tenant lookup and forwards the bearer token, session cookie and
correlation ID. Redirects, pooled cookies and system proxies are disabled for this hop.
For streaming responses it forwards complete SSE events as they arrive, removes server-side
tool-call deltas, and adds the OpenAI `[DONE]` marker after a successful terminal event when
the upstream omitted it. An incomplete or failed upstream stream is not marked successful.
The upstream request and response body share a five-minute timeout.

For a gateway listening directly over HTTPS, the listener certificate must also be trusted
for `localhost`. HTTP listeners that redirect the internal hop to HTTPS are not supported.

These endpoints are exercised by integration and Playwright tests under `test/`.

## Authentication

The gateway currently registers the `Bearer` authentication scheme and ASP.NET authorization middleware.

`POST /api/documents/upload` requires authorization and accepts multipart form values:

- `file` (required)
- `channel_id` (required GUID)
- `availability_scope` (optional: `tenant`, `user`, or `channel`; defaults to `user`)

Uploads are staged and enqueued for asynchronous ingestion. The endpoint returns `202 Accepted` when a job is queued.

Reference: [`../../src/Services/LeanKernel.Services.Gateway/Program.cs`](../../src/Services/LeanKernel.Services.Gateway/Program.cs)

## CORS

The gateway currently applies a permissive local policy named `AllowLocal`.

## Development UI

In Development, `AddDevUI` and `MapDevUI` are enabled.

This is a host-time inspection surface for the named agent runtime, not a general product UI.
