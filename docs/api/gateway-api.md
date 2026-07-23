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

Reference: [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs)

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

Current implementation note: the chat-completions proxy path is currently forwarded to `http://localhost:8080` by the endpoint helper.

These endpoints are exercised by integration and Playwright tests under `test/`.

## Authentication

The gateway currently registers the `Bearer` authentication scheme and ASP.NET authorization middleware.

`POST /api/documents/upload` requires authorization and accepts multipart form values:

- `file` (required)
- `channel_id` (required GUID)
- `availability_scope` (optional: `tenant`, `user`, or `channel`; defaults to `user`)

Uploads are staged and enqueued for asynchronous ingestion. The endpoint returns `202 Accepted` when a job is queued.

Reference: [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs)

## CORS

The gateway currently applies a permissive local policy named `AllowLocal`.

## Development UI

In Development, `AddDevUI` and `MapDevUI` are enabled.

This is a host-time inspection surface for the named agent runtime, not a general product UI.
