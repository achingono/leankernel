# Web Search Provider Fallback PRD

## Summary
Implement provider selection for the built-in `web_search` tool:
- Use Brave Search when `BRAVE_API_KEY` environment variable is present.
- Otherwise, use DuckDuckGo.

This keeps the tool contract unchanged (`web_search` with `query` parameter) while adding provider flexibility.

## Goals
- Preserve existing tool name and query parameter behavior.
- Add Brave provider support requiring API key.
- Maintain DuckDuckGo as no-key default provider.
- Return useful and readable output from either provider.
- Surface actionable errors for HTTP or parsing failures.

## Non-Goals
- No new configuration surface beyond `BRAVE_API_KEY` env var.
- No new dependencies.
- No changes to tool registration shape or external API.

## Design
1. Update `WebSearchTool` handler logic to:
   - Validate `query`.
   - Read `BRAVE_API_KEY` via `Environment.GetEnvironmentVariable("BRAVE_API_KEY")`.
   - If key is set: call Brave endpoint with required header `X-Subscription-Token`.
   - If key is missing: call DuckDuckGo endpoint.
2. Add provider-specific helpers:
   - `SearchWithBraveAsync(HttpClient, query, apiKey, ct)`
   - `SearchWithDuckDuckGoAsync(HttpClient, query, ct)`
3. Parse Brave JSON (`web.results[]`) and render compact multi-line results.
4. Keep existing DuckDuckGo answer behavior (prefer `AbstractText`, then `Answer`, then fallback text).
5. Wrap network/parsing operations in error handling and return failed `ToolResult` with provider-specific context.

## Risks and Mitigations
- Brave schema differences: use defensive JSON access (`TryGetProperty`) and fallback text.
- Provider failure clarity: include provider name and HTTP status/exception in error output.
- Behavior regression: preserve DuckDuckGo output semantics when Brave key is absent.

## Test Strategy
- Build impacted project(s): `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
- If existing tool unit-test coverage pattern is straightforward, add tests later in follow-up.
- Verify manual behavior paths:
  - No `BRAVE_API_KEY` => DuckDuckGo path.
  - With `BRAVE_API_KEY` => Brave path.
  - Empty query => validation error.

## Review Notes (Independent Plan Review)
- Use `HttpRequestMessage` for Brave so headers can be attached.
- Keep fallback strategy explicit: key present => Brave; key absent => DuckDuckGo.
- Add robust provider-specific exception handling.
