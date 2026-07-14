# Browser Built-in Tool + Playwright Service PRD

## Summary
Add a new LeanKernel built-in tool family that executes browser automation through a **separate HTTP Playwright service container**.  
Emanate is reference material for browser/session API patterns; implementation remains LeanKernel-native.

## Goals
- Add production-safe browser automation tool(s) in the LeanKernel tool registry.
- Integrate with an external Playwright service over HTTP (container boundary).
- Adopt Webwright-inspired execution concepts:
  - code-as-action execution artifacts,
  - screenshot/action-log evidence,
  - explicit checkpoint verification.
- Provide deterministic run lifecycle, async-friendly polling, and structured error mapping for agent use.

## Non-Goals
- Do not embed Playwright runtime directly inside LeanKernel process.
- Do not copy Emanate code into LeanKernel.
- Do not implement broad autonomous crawling in v1.
- Do not implement persistent multi-session login flows in v1 (each run is stateless).

## Current-State Analysis
### LeanKernel (target)
- Tool framework exists (`ToolDefinition`, `ToolRegistry`, `ToolExecutor`).
- Built-ins are registered centrally in `ToolsServiceCollectionExtensions`.
- Existing internet tools (`web_fetch`, `http_request`) demonstrate:
  - parameter validation,
  - SSRF protections,
  - JSON-serialized structured output,
  - test scaffolding (`IServiceScopeFactory` with stub `HttpMessageHandler`).
- Hardening primitives available in `LeanKernelHardeningServiceCollectionExtensions`:
  - named `IHttpClientFactory` clients with `Bearer` auth,
  - `CorrelationIdDelegatingHandler` auto-attached to every client,
  - `IProviderHealthProbe` pattern (`LiteLlmHealthProbe`, `GBrainHealthProbe`),
  - `ResilienceConfig` (retry, circuit breaker, timeout) and `RateLimitConfig`.
- Convention: every existing built-in tool is **single-purpose** (one operation per tool).

### Emanate (reference)
- `createStealthContext` and session lifecycle model demonstrate browser reliability patterns.
- `ResearchRateLimiter` (1 concurrent per platform, cooldown, queue timeout) demonstrates per-key serialization.
- Route organization and research endpoints demonstrate schema/error/concurrency guardrails.

## Architectural Decisions (resolved gaps)

### A. Tool granularity — split per operation
**Decision:** Ship **four single-purpose tools**, not one multi-op `browser` tool. Matches the codebase convention and lets `ToolGovernancePolicy` allow/deny each operation independently (e.g. expose `browser_get_run` widely but gate `browser_run_script`).
- `browser_run_task` — natural-language task → script run.
- `browser_run_script` — explicit Playwright script (gated; off by default).
- `browser_get_run` — fetch status + artifact manifest for a run.
- `browser_get_artifact` — fetch a specific artifact (screenshot/log) by run+id, base64-encoded when binary.

### B. Execution model — async with required polling
**Decision:** `POST /runs` returns **immediately** with `runId` and `status=queued|running`. The tool **does not block** for completion. Agents (or `WorkerAgent` loops) poll via `browser_get_run`.
Rationale: real browser tasks routinely exceed 60s; blocking the tool call breaks the HTTP timeout model and ties up the chat turn. Webwright's loop already pattern-matches this: model issues command → observes → issues next.

Optional v1.1 follow-up: `wait_seconds` parameter on `browser_get_run` for short server-side long-polling (max 25s) to reduce poll churn.

### C. Browser-state lifecycle — stateless per run (v1)
**Decision:** Each run launches a fresh Playwright context. **No persistent sessions, no login flows in v1.** Aligns with Webwright; sidesteps the credential-handling surface area that dominates Emanate's complexity. Persistent session profiles deferred to a later PRD.

### D. Artifact access — service-served URLs + dedicated fetch tool
**Decision:** Browser service serves artifacts at stable paths (`/runs/{runId}/artifacts/{artifactId}`). The run manifest returns relative paths + content-type + byte size. Agents fetch screenshots via `browser_get_artifact` (returns base64 + content-type) — they are **not** inlined into every status response.

### E. Service ownership and language
**Decision:** Browser service lives in this repository under `services/browser-service/` and is a **Node.js + Playwright** service (TypeScript). Rationale: closest mapping to Emanate reference code, smallest Playwright surface to maintain, no Python runtime added to the stack. Service Dockerfile uses the official `mcr.microsoft.com/playwright:v1.x-jammy` base image.

### F. Tool output shape
**Decision:** Tool handlers return `ToolResult.Output` as a **JSON-serialized object** (camelCase), matching `HttpRequestTool`. Shape per tool documented below; agents are instructed via tool descriptions to parse JSON.

### G. Cancellation contract
**Decision:** `CancellationToken` from `IToolExecutor` triggers `DELETE /runs/{runId}` on the browser service (best-effort, non-blocking). Browser service treats `DELETE` as a stop signal and marks run `cancelled`.

### H. Concurrency control
**Decision:** Browser service enforces a global semaphore (default 4 concurrent runs, configurable) and per-`requestKey` serialization (1 concurrent per key) modeled on Emanate's `ResearchRateLimiter`. `requestKey` is supplied by the tool caller (e.g. session ID) and defaults to a per-process bucket. LeanKernel side adds **no** in-process limit; relies on service.

### I. Authentication
**Decision:** Static `Bearer` token in `Authorization` header (v1). Token sourced from `LeanKernel:BrowserService:ApiToken` configuration, overridable via standard `LEANKERNEL__BROWSERSERVICE__APITOKEN` environment variable. mTLS is a deferred follow-up.

### J. Observability and correlation
**Decision:** Register the browser-service client through `IHttpClientFactory` so `CorrelationIdDelegatingHandler` is auto-applied (see `ConfigureAll<HttpClientFactoryOptions>` in `LeanKernelHardeningServiceCollectionExtensions`). Tool handler creates an `ActivitySource("LeanKernel.Tools.Browser")` span per call. Add a `BrowserServiceHealthProbe : IProviderHealthProbe` that hits `/health` on the service and is registered alongside the existing probes.

## Tool Contracts (LeanKernel side)

All four tools register under `Category = "browser"` so operators can allow the whole family via `ToolVisibilityContext.AllowedCategories`.

### `browser_run_task`
| Param | Type | Required | Notes |
|---|---|---|---|
| `task` | string | yes | Natural-language task |
| `start_url` | string | no | Initial navigation target (must be absolute http/https) |
| `checkpoint_labels` | string[] | no | Labels the agent expects to verify |
| `request_key` | string | no | Idempotency / serialization key |
| `request_id` | string | no | Idempotency key for retry-safe submission |

Output: `{ runId, status, submittedAt }`

### `browser_run_script`
Same as `run_task` but with `script` (string, required) instead of `task`. **Disabled by default** — operators must opt in via `AllowedToolNames` or `AllowedCategories`.

### `browser_get_run`
| Param | Type | Required | Notes |
|---|---|---|---|
| `run_id` | string | yes | |
| `wait_seconds` | int | no | Server-side long-poll up to 25s (v1.1) |

Output:
```json
{
  "runId": "...",
  "status": "queued|running|succeeded|failed|cancelled|timed_out",
  "startedAt": "...",
  "completedAt": "...",
  "result": { "finalText": "...", "data": { ... } },
  "checkpoints": [ { "label": "...", "passed": true, "evidenceArtifactId": "..." } ],
  "artifacts": [ { "id": "...", "kind": "screenshot|log|script", "path": "...", "contentType": "...", "bytes": 12345 } ],
  "error": { "code": "...", "message": "..." }
}
```

### `browser_get_artifact`
| Param | Type | Required | Notes |
|---|---|---|---|
| `run_id` | string | yes | |
| `artifact_id` | string | yes | |
| `max_bytes` | int | no | Cap; default from `MaxArtifactBytes` config |

Output: `{ runId, artifactId, contentType, base64, truncated }`

## Browser Service API Contract (container)
- `POST /runs` → `202 Accepted`, body `{ runId, status, submittedAt }`
- `GET  /runs/{runId}` → run status + artifact manifest
- `GET  /runs/{runId}/artifacts/{artifactId}` → raw artifact (binary or text)
- `DELETE /runs/{runId}` → cancel signal (idempotent)
- `GET  /health` → `200 {status:"ok"}` for `IProviderHealthProbe`

### Error envelope (expanded)
```json
{ "code": "...", "message": "...", "details": { ... } }
```
Codes:
- `VALIDATION_ERROR`
- `UNAUTHORIZED`
- `NOT_FOUND` (unknown runId / artifactId)
- `TIMEOUT`
- `NAVIGATION_BLOCKED` (domain policy)
- `LIMIT_EXCEEDED` (steps/payload/global concurrency)
- `CANCELLED`
- `BAD_GATEWAY` (upstream site error surfaced)
- `SERVICE_UNAVAILABLE` (LeanKernel maps `HttpRequestException` / circuit-open)
- `INTERNAL_ERROR`

## Runtime Model (Webwright-inspired)
- Every run produces:
  - action log (jsonl, append-only),
  - checkpoint screenshots,
  - final structured result,
  - optional generated script snapshot for `run_task`.
- Checkpoints are explicit and addressable from the manifest.
- Runs are idempotent when `request_id` is reused within the retention window.
- Browser context is **fresh per run** (no shared cookies/storage in v1).

## Security and Governance
- **LeanKernel side:**
  - `browser_run_script` defaults to **off** in `ToolGovernancePolicy`.
  - Input URL validation reuses `web_fetch` / `http_request` SSRF guard helpers (move shared helpers to `BuiltIn/Common/UrlSafety.cs` if duplicated).
  - Tool input size capped (`task` ≤ 4 KB, `script` ≤ 32 KB).
- **Browser service side:**
  - Domain allowlist (default empty = block-all) and denylist for known-bad hosts.
  - Per-run step limit, total wall-clock timeout, max artifact bytes per run.
  - Secret redaction in logs (regex-based: API keys, bearer tokens, common cookie names).
  - Disallowed protocols: `file:`, `ftp:`, `data:` (except for inline asset capture).
  - Artifact retention: configurable TTL (default 24h) + max artifacts per run.
- **Network/auth:**
  - Service token (v1 baseline); environment-variable injection only.
  - mTLS deferred.

## Configuration
Add a new section to `appsettings.json` under `LeanKernel`:

```json
"BrowserService": {
  "BaseUrl": "http://browser-service:3000",
  "ApiToken": "",
  "RequestTimeoutSeconds": 15,
  "MaxArtifactBytes": 2000000,
  "MaxOutputChars": 12000,
  "AllowRunScript": false,
  "HealthProbe": { "Enabled": true }
}
```

Add a `BrowserServiceConfig` class to `LeanKernel.Abstractions/Configuration/` and reference it from `LeanKernelConfig`. `RequestTimeoutSeconds` applies to LeanKernel↔service calls (short, since the service is async); long-running run execution is bounded by service-side limits.

## docker-compose integration
Add a new service to `docker-compose.yml` (current stack: `engine`, `database`, `litellm`, etc.):

```yaml
services:
  browser-service:
    build: ./services/browser-service
    environment:
      - PORT=3000
      - API_TOKEN=${BROWSER_SERVICE_API_TOKEN}
      - MAX_CONCURRENT_RUNS=4
      - ARTIFACT_TTL_HOURS=24
    volumes:
      - browseradata:/app/data/runs
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:3000/health"]
      interval: 30s
      timeout: 5s
      retries: 3

volumes:
  browseradata:
```

The `engine` service gains a `depends_on: browser-service` and an env var `LEANKERNEL__BROWSERSERVICE__BASEURL=http://browser-service:3000`.

## Implementation Plan
1. Add `BrowserServiceConfig` to `LeanKernel.Abstractions/Configuration/` and wire it into `LeanKernelConfig`.
2. Add typed HTTP client and DTOs in `LeanKernel.Tools/BuiltIn/Browser/`:
   - `IBrowserServiceClient` (interface), `BrowserServiceClient` (impl using named `HttpClient`).
   - Request/response DTOs and error mapper.
3. Add four tool definitions in the same folder, each as `static class`:
   - `BrowserRunTaskTool`, `BrowserRunScriptTool`, `BrowserGetRunTool`, `BrowserGetArtifactTool`.
4. Register tools in `ToolsServiceCollectionExtensions`. Also register:
   - named `HttpClient` for browser service (with `Bearer` token + base URL),
   - `BrowserServiceClient` as singleton,
   - `BrowserServiceHealthProbe` in `LeanKernelHardeningServiceCollectionExtensions` alongside existing probes.
5. Add `services/browser-service/` (Node.js + TypeScript + Playwright):
   - HTTP server, run queue, artifact store on `/app/data/runs/<runId>/`,
   - Playwright launcher with stealth defaults inspired by Emanate's `createStealthContext`,
   - cancellation, retention sweep, health endpoint,
   - Dockerfile based on `mcr.microsoft.com/playwright:v1.x-jammy`.
6. Update `docker-compose.yml` with the new service + env wiring.
7. Add unit tests in `test/LeanKernel.Tests.Unit/Tools/Browser/`:
   - validation failures per tool,
   - success mapping for each tool,
   - cancellation triggers `DELETE`,
   - timeout/service-unavailable/error-code mapping.
8. Add integration tests in `test/LeanKernel.Tests.Integration/` against a stub HTTP server (no real Playwright).
9. Add Playwright-driven end-to-end tests under `test/LeanKernel.Tests.Playwright/` exercising the real `browser-service` against a sandbox HTML fixture.
10. Update docs: `README.md`, `docs/features/browser-tool.md`, `docs/development/`, configuration reference, and `docs/plans/index.md` entry (already added).

## Validation Plan
- `dotnet restore src/LeanKernel.sln`
- `dotnet build src/LeanKernel.sln --no-restore -v minimal`
- `dotnet test src/LeanKernel.sln --no-build -v minimal`
- `scripts/quality/test-coverage.sh`
- `scripts/quality/sonarqube-scan.sh`
- `docker compose build`
- `docker compose up -d browser-service && curl -fsS http://localhost:3000/health` (smoke)

## Risks and Mitigations
- **Service dependency risk:** Browser service downtime blocks tool calls.
  - Mitigation: short LeanKernel-side timeout (15s) → fast `SERVICE_UNAVAILABLE`; `BrowserServiceHealthProbe` surfaces status via `/api/health`; degradation policy can hide the tools.
- **Security risk (navigation abuse):** untrusted targets and data exfiltration.
  - Mitigation: empty allowlist by default, denylist for known-bad, protocol restrictions, redaction, bounded outputs.
- **Long-running run risk:** runs exceed HTTP timeout.
  - Mitigation: async submit-then-poll contract is the primary design; no long-blocking tool call.
- **Artifact growth risk:** screenshot/log accumulation.
  - Mitigation: TTL sweep + max artifacts/run + cap on per-fetch bytes.
- **`run_script` abuse risk:** arbitrary Playwright code can do destructive things.
  - Mitigation: gated off by default; even when on, browser service runs scripts in a sandboxed Node VM with restricted globals and the same domain/timeout limits as `run_task`.
- **Cancellation leak:** abandoned LLM tool calls leave browser tabs open.
  - Mitigation: `CancellationToken` → `DELETE /runs/{runId}` + service-side reaper that kills runs idle past max wall-clock.

## Acceptance Criteria
- Four browser tools appear in registry and execute through `IToolExecutor`.
- Each tool validates arguments per its contract above.
- `browser_run_script` is hidden unless explicitly allowed.
- Browser service errors are mapped to the documented error code table.
- Run status responses include artifact references and checkpoint evidence metadata.
- Cancellation propagates from tool to service.
- `BrowserServiceHealthProbe` reports through `/api/health`.
- Tests and quality gates pass under repository validation sequence.

## Open Decisions (deferred)
- Should `request_key` default to the calling `sessionId` automatically (cross-cutting plumbing)?
- Retention TTL default (24h proposed) — confirm against compliance requirements.
- Whether to add a SpendGuard-style budget for browser runs (separate from token spend).

## Independent Plan Review Notes
- Keep LeanKernel focused on tool orchestration; avoid embedding Playwright directly.
- Preserve deterministic, inspectable outputs to support debugging and trust.
- Start with service-token auth and add mTLS later only if operationally required.
- Single-purpose tools and async submit-then-poll are the two non-negotiable architectural choices for v1.
