# Gateway API

The current HTTP surface is hosted by `LeanKernel.Gateway`.

## Mapped Endpoints

The composition root currently maps:

- `MapOpenAIResponses()`
- `MapOpenAIConversations()`
- `MapProxiedOpenAIChatCompletions()` at `/v1/internal/completions`
- `GET /health`
- `MapDevUI()` in Development only

Reference: [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs)

## Health Endpoint

`GET /health`

Returns a small JSON payload with runtime status and a timestamp.

## OpenAI-Compatible Endpoints

The exact route set for responses and conversations is owned by the MAF hosting extensions, not handwritten controller code.

Current expectation:

- `/v1/responses`
- `/v1/conversations`

These endpoints are exercised by integration and Playwright tests under `test/`.

## Authentication

The gateway currently registers the `Bearer` authentication scheme and ASP.NET authorization middleware.

Reference: [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs)

## CORS

The gateway registers a permissive local policy named `AllowLocal`.

## Development UI

In Development, `AddDevUI` and `MapDevUI` are enabled.

This is a host-time inspection surface for the named agent runtime, not a general product UI.
