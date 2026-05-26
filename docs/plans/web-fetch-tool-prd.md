# Web Fetch Tool PRD

## Summary
Add a new built-in tool, `web_fetch`, that retrieves web content from a URL for agent use.

## Goals
- Introduce `web_fetch` with a stable tool contract.
- Validate input URL and prevent obvious unsafe local targets.
- Fetch content over HTTP(S) and return readable text output.
- Return clear failure messages for invalid input and HTTP/network errors.
- Register tool in built-in tool list.

## Non-Goals
- No HTML-to-markdown conversion pipeline.
- No crawler behavior or multi-page traversal.
- No new external packages.

## Design
1. Create `src/LeanKernel.Tools/BuiltIn/WebFetchTool.cs`.
2. Define tool metadata:
   - Name: `web_fetch`
   - Category: `internet`
   - Parameter: `url` (required string)
3. Handler flow:
   - Validate required URL.
   - Ensure absolute HTTP/HTTPS URI.
   - Block obvious local/private literal IP targets and `localhost`.
   - Send GET request via scoped `HttpClient`.
   - Require success status.
   - Accept text-like responses (`text/*`, `application/json`, `application/xml`, `application/xhtml+xml`).
   - Read body and truncate output to 20,000 chars with notice if exceeded.
4. Add provider-neutral exception handling with actionable error text.
5. Register `WebFetchTool` in `ToolsServiceCollectionExtensions`.

## Risks and Mitigations
- SSRF risk: block localhost/private literal IP addresses.
- Binary payload abuse: reject non-text content types.
- Very large payloads: truncate to max output length.

## Test Strategy
- Add unit tests in `test/LeanKernel.Tests.Unit/Tools/WebFetchToolTests.cs`:
  - missing URL validation
  - invalid URL validation
  - localhost blocked
  - successful fetch returns content
  - HTTP error returns failure
  - large response is truncated with marker
- Validate build for impacted project(s).

## Independent Plan Review Notes
- Use `HttpRequestMessage` with User-Agent header.
- Include explicit truncation marker in returned output.
- Keep error messages provider/tool-specific and actionable.
