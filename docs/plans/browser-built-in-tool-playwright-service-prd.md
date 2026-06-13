# Browser Built-in Tool via Webwright Sidecar PRD

## Summary
Add a LeanKernel built-in tool family for browser automation, implemented by wrapping **Microsoft Research's Webwright** in a sidecar HTTP service. LeanKernel exposes deterministic `browser_*` tools; the sidecar shells out to Webwright's agent loop, which drives Playwright and produces re-runnable artifacts.

Emanate remains design reference for stealth and rate-limiting patterns; Webwright is the **execution engine**.

## Goals
- Add production-safe browser automation tool(s) in the LeanKernel tool registry.
- Reuse Webwright's evaluated agent loop, workspace contract, and artifact format instead of reimplementing them.
- Route Webwright's LLM calls through LeanKernel's LiteLLM using a dedicated scoped LiteLLM key so model routing, browser-specific budget limits, and observability remain centralized at the proxy boundary.
- Provide an async submit-then-poll tool contract with structured error mapping.

## Non-Goals
- Do not embed Playwright in the LeanKernel process.
- Do not reimplement Webwright's task-script-verify loop.
- Do not implement persistent multi-session login flows in v1 (Webwright is intentionally stateless per run).
- Do not expose Webwright's full CLI surface to agents; only the curated tool contract below.

## Why Webwright (vs hand-rolled runtime)
Webwright (Microsoft Research, MIT, released 2026-05) is ~1.5k LoC and ships exactly the runtime the previous PRD was reinventing:
- `task → final_script.py → screenshots → self-verify` loop.
- `final_runs/run_<id>/` workspace contract (script + screenshots + action log).
- OpenAI-compatible model backends (so LiteLLM works directly).
- Skill/plugin-style invocation already supported.
- Public benchmarks: 86.7% on Online-Mind2Web, 60.1% on Odysseys (100-step budget).

Trade-offs accepted:
- New project (May 2026) — pinned to a specific version, sidecar API kept engine-agnostic so we can swap later.
- Python in the sidecar — contained behind HTTP; no LeanKernel-side Python dependency.
- Opinionated defaults (Firefox headless, 1280×1800 viewport) — acceptable for v1.

## Current-State Analysis
### LeanKernel (target)
- Tool framework: `ToolDefinition`, `ToolRegistry`, `ToolExecutor`, and `ToolsServiceCollectionExtensions.AddLeanKernelTools`.
- Hardening primitives in `LeanKernel.Gateway/LeanKernelHardeningServiceCollectionExtensions`:
  - named `IHttpClientFactory` clients with `Bearer` auth,
  - `CorrelationIdDelegatingHandler` auto-attached,
  - `IProviderHealthProbe` pattern (`LiteLlmHealthProbe`, `GBrainHealthProbe`),
  - `ResilienceConfig`, `RateLimitConfig`.
- Existing internet tools (`web_fetch`, `http_request`) show single-purpose tool definitions, JSON-output, URL guard, and stub-handler test patterns.
- Convention: single-purpose tools, one operation per tool.

### Webwright (engine)
- CLI: `python -m webwright.run.cli -c base.yaml -c model_openai.yaml -t "<task>" --start-url <url> --task-id <id> -o <outDir>`.
- Configs are stackable YAML (`base.yaml` + model overlay + optional `task_showcase.yaml`).
- Workspace output: `outputs/<task_id>/final_runs/run_<n>/{final_script.py, final_script_log.txt, screenshots/*.png}` (+ optional `report.json` in task_showcase mode).
- Backends: OpenAI / Anthropic / OpenRouter — any OpenAI-compatible endpoint works for the OpenAI backend, so **LiteLLM is a drop-in**.
- Each run launches a fresh Firefox (no persistent state).

## Architecture
```
┌────────────────────────────────────────────────────────────────┐
│ LeanKernel (engine container)                                  │
│                                                                │
│  browser_run_task ──┐                                          │
│  browser_get_run ───┼──► IWebwrightClient (HttpClient)    │
│  browser_get_artifact│                                         │
│                     │                                          │
└─────────────────────┼──────────────────────────────────────────┘
                      │ HTTP + Bearer + correlation
                      ▼
┌────────────────────────────────────────────────────────────────┐
│ webwright container (Python + Webwright + Playwright)    │
│                                                                │
│  FastAPI app                                                   │
│   POST /runs ─────► RunManager (queue, semaphore)              │
│                       │                                        │
│                       └─► subprocess: python -m webwright...   │
│                              │                                 │
│                              ├─► Webwright agent loop ◄────┐   │
│                              │                             │   │
│                              └─► Playwright (Firefox)      │   │
│                                                            │   │
│   GET /runs/{id}        ◄── reads outputs/<id>/...         │   │
│   GET /runs/{id}/artifacts/<artifactId>                    │   │
│   DELETE /runs/{id}     ◄── SIGTERM webwright process      │   │
│   GET /health                                              │   │
│                                                            │   │
│  Webwright OpenAI backend points to: http://litellm:4000 ──┘   │
└────────────────────────────────────────────────────────────────┘
```

## Architectural Decisions

### A. Tool granularity — split per operation (unchanged)
Four single-purpose tools registered under `Category = "browser"`:
- `browser_run_task` — submit a natural-language browser task.
- `browser_get_run` — poll status + artifact manifest.
- `browser_get_artifact` — fetch one artifact (base64 for binaries).
- `browser_cancel_run` — request cancellation.

**Dropped:** `browser_run_script`. Webwright's agent already authors `final_script.py`; exposing a raw-script bypass adds attack surface without clear v1 value. Re-evaluate post-v1.

### B. Execution model — async submit-then-poll
Webwright CLI is a blocking process that may run 30s–10min. The sidecar runs it as a background subprocess and exposes async status:
- `POST /runs` → `202 Accepted`, returns `runId` immediately.
- LeanKernel tool **does not block** waiting for completion.
- Agents poll `browser_get_run` until terminal state.

### C. LLM routing and spend governance — Webwright → LiteLLM
The webwright container ships a Webwright config that pins the OpenAI backend's `base_url` to LeanKernel's LiteLLM (`http://litellm:4000`). This means:
- Every Webwright LLM call is routed/budgeted/observed by LiteLLM.
- Model choice (`gpt-4o`, `claude-sonnet`, etc.) is controlled centrally.
- Provider API keys remain centralized in LiteLLM; the browser service receives a dedicated LiteLLM virtual key, not provider keys and not the LiteLLM master key.
- Browser-driven spend is enforced by LiteLLM virtual-key budget, model allowlist, tags, and metadata. LeanKernel's in-process `ISpendTracker` does not see Webwright's direct proxy calls unless a future reconciliation job imports LiteLLM usage records.

Config injected at container start:
```yaml
# /app/config/leankernel.yaml (overlay on top of webwright base.yaml)
model:
  backend: openai
  base_url: ${LITELLM_BASE_URL}        # http://litellm:4000
  api_key:  ${LITELLM_API_KEY}         # scoped virtual key for webwright
  model:    ${WEBWRIGHT_MODEL}         # e.g. gpt-4o
```

### D. Browser-state lifecycle — stateless per run
Inherits Webwright's per-run fresh Firefox model. Persistent sessions deferred.

### E. Artifact access — manifest-gated file access
Webwright writes to `outputs/<task_id>/final_runs/run_<n>/`. The sidecar exposes only files that appear in a server-built artifact manifest:
- Manifest is computed by listing known artifact locations in the run folder and classifying (`final_script.py`, `final_script_log.txt`, `screenshots/*.png`).
- Artifact IDs are opaque or sanitized manifest IDs, not caller-supplied paths. The API consistently uses `{artifactId}` rather than `{artifactPath}`.
- The sidecar canonicalizes every artifact target and verifies it remains under the selected run directory before reading bytes. Unknown IDs, traversal attempts, symlinks escaping the run directory, and absolute paths return `NOT_FOUND`.
- `GET /runs/{runId}/artifacts/{artifactId}` streams the file with correct `Content-Type`; `browser_get_artifact` base64-encodes binaries.

### F. Service ownership and image
Service lives in this repository under `services/webwright/`:
- Base image: `mcr.microsoft.com/playwright/python:v1.49.1-jammy` (matches Webwright's Playwright pin).
- Dependencies: `webwright==<pinned>`, `fastapi`, `uvicorn`.
- Webwright installed via `pip install webwright` (or git pin) plus `playwright install firefox`.

### G. Tool output shape
JSON-serialized objects in `ToolResult.Output`, matching `HttpRequestTool` convention. Shapes documented per tool below.

### H. Cancellation contract
Run cancellation is explicit. `browser_cancel_run` sends `DELETE /runs/{runId}`; the sidecar sends `SIGTERM` to the Webwright subprocess and the reaper kills any orphans past wall-clock limit.

`CancellationToken` from `IToolExecutor` only cancels the current HTTP call (`POST /runs`, `GET /runs/{runId}`, or artifact download). If `browser_run_task` is cancelled after the sidecar has accepted a run and returned a `runId`, the tool makes a best-effort `DELETE /runs/{runId}` before surfacing cancellation; otherwise the run remains cancellable through `browser_cancel_run`.

### I. Queue and concurrency control
Sidecar-side bounded queue, global semaphore (default 2 concurrent runs — browsers are heavy), and per-`requestKey` serialization (1 per key, modeled on Emanate's `ResearchRateLimiter`). `POST /runs` returns `queued` when accepted. Saturated queues return `429` or `503` with `LIMIT_EXCEEDED`/`SERVICE_UNAVAILABLE` rather than accepting unbounded work. LeanKernel side adds no in-process limit.

### I.1 Idempotency and restart recovery
`request_id` is an idempotency key. A duplicate `request_id` with the same normalized payload returns the existing run; the same `request_id` with a different payload returns `CONFLICT`. The sidecar persists enough request metadata under the output root to avoid duplicating accepted submissions after restart.

On sidecar startup, the service reconciles the output directory with in-memory state:
- completed runs with artifacts remain discoverable until TTL cleanup,
- runs with no active subprocess are marked `failed` with `SERVICE_RESTARTED` (or `timed_out` if past wall-clock),
- queued work without a started subprocess may be requeued only if the persisted payload is complete and idempotency checks pass.

### J. Authentication
Static `Bearer` token, env var `LEANKERNEL__WEBWRIGHT__APITOKEN`. mTLS deferred. `/health` is unauthenticated and liveness-only so container health checks work without secrets; operational endpoints and `/ready` require bearer auth.

### K. Observability
- Browser-service HTTP client registered via `IHttpClientFactory` → `CorrelationIdDelegatingHandler` auto-applies.
- Tool handlers emit traces under `ActivitySource("LeanKernel.Tools.Browser")`.
- `WebwrightHealthProbe : IProviderHealthProbe` hits authenticated `/ready`, registered alongside existing probes.
- Sidecar logs Webwright stdout/stderr with run-id prefix; correlation ID propagated as `X-Correlation-Id` and embedded in subprocess env.

## Tool Contracts (LeanKernel side)

All four tools register under `Category = "browser"`. None expose Webwright internals beyond what's needed.

### `browser_run_task`
| Param | Type | Required | Notes |
|---|---|---|---|
| `task` | string | yes | Natural-language task; ≤ 4 KB |
| `start_url` | string | no | Absolute http/https; passed to Webwright `--start-url` |
| `model` | string | no | Override LiteLLM model alias for this run |
| `request_key` | string | no | Concurrency/serialization key |
| `request_id` | string | no | Idempotency key |

Output: `{ runId, status, submittedAt, queuePosition? }`

Duplicate `request_id` handling is deterministic: same normalized payload returns the existing run, different payload returns `CONFLICT`.

### `browser_get_run`
| Param | Type | Required |
|---|---|---|
| `run_id` | string | yes |
| `wait_seconds` | int | no — reserved for server-side long-poll up to 25s in v1.1; ignored or rejected in v1 |

Output:
```json
{
  "runId": "...",
  "status": "queued|running|succeeded|failed|cancelled|timed_out",
  "startedAt": "...",
  "completedAt": "...",
  "exitCode": 0,
  "finalDatum": "...",
  "artifacts": [
    { "id": "script",       "kind": "script",     "displayName": "final_script.py",       "contentType": "text/x-python", "bytes": 12345 },
    { "id": "log",          "kind": "log",        "displayName": "final_script_log.txt",  "contentType": "text/plain",    "bytes": 4321 },
    { "id": "screenshot-3", "kind": "screenshot", "displayName": "screenshots/step_3.png","contentType": "image/png",     "bytes": 87654 }
  ],
  "error": { "code": "...", "message": "..." }
}
```

`finalDatum` is the sidecar-normalized final answer or task summary extracted from the execution engine's outputs; null if the run failed before producing one. The LeanKernel tool contract does not depend on a specific Webwright log layout.

### `browser_get_artifact`
| Param | Type | Required | Notes |
|---|---|---|---|
| `run_id` | string | yes | |
| `artifact_id` | string | yes | Opaque/sanitized ID that must match an `id` from the run manifest |
| `max_bytes` | int | no | Cap; default from `MaxArtifactBytes` config |

Output: `{ runId, artifactId, contentType, base64, truncated }` (text artifacts are base64-encoded too for uniformity).

For text artifacts, `max_bytes` may return a truncated base64-encoded UTF-8 prefix with `truncated = true`. For binary artifacts, exceeding `max_bytes` returns `LIMIT_EXCEEDED` instead of emitting undecodable truncated bytes.

### `browser_cancel_run`
| Param | Type | Required |
|---|---|---|
| `run_id` | string | yes |

Output: `{ runId, status, message }`

## Browser Service API Contract
- `POST   /runs` — enqueue a Webwright run. Body: `{ task, startUrl?, model?, requestKey?, requestId? }`. Returns `202 { runId, status: "queued", submittedAt, queuePosition? }`; returns `409 CONFLICT` for idempotency payload mismatch and `429`/`503` when queue limits reject work.
- `GET    /runs/{runId}` — status + manifest.
- `GET    /runs/{runId}/artifacts/{artifactId}` — stream a manifest-listed artifact by opaque/sanitized ID.
- `DELETE /runs/{runId}` — cancel (idempotent).
- `GET    /health` — unauthenticated liveness: `{ status: "ok" }`.
- `GET    /ready` — authenticated readiness/details: `{ status, webwrightVersion, playwrightVersion, liteLlmReachable, queueDepth }`.

### Error envelope
```json
{ "code": "...", "message": "...", "details": { ... } }
```
Codes: `VALIDATION_ERROR`, `UNAUTHORIZED`, `NOT_FOUND`, `CONFLICT`, `TIMEOUT`, `LIMIT_EXCEEDED`, `CANCELLED`, `WEBWRIGHT_FAILED`, `SERVICE_RESTARTED`, `SERVICE_UNAVAILABLE`, `INTERNAL_ERROR`.

`WEBWRIGHT_FAILED` wraps non-zero subprocess exit codes with the tail of stderr in `details`.

## Runtime Flow
1. Agent calls `browser_run_task` → tool POSTs to sidecar with submitted task.
2. Sidecar validates input, applies idempotency rules, allocates `runId = task_id`, creates `outputs/<runId>/`, persists request metadata, queues the run, and returns `202`.
3. A worker acquires global + per-key semaphore and spawns `python -m webwright.run.cli -c base.yaml -c model_openai.yaml -c leankernel.yaml -t "<task>" --task-id <runId> -o outputs/`.
4. Webwright drives the loop; LLM calls hit LiteLLM via the injected `base_url`; Playwright drives Firefox; artifacts land in `outputs/<runId>/final_runs/run_<n>/`.
5. Agent polls `browser_get_run`; sidecar reports `queued` until a worker starts and `running` until subprocess exits.
6. On exit, sidecar walks the latest `final_runs/run_<n>/` folder, builds the manifest, extracts a sidecar-normalized `finalDatum` from engine outputs, and sets terminal status.
7. Agent fetches needed artifacts via `browser_get_artifact` (e.g. final screenshot for visual verification, log for audit).
8. Retention sweeper deletes runs older than TTL.

## Security and Governance
- **LeanKernel side:**
  - Tools registered under `Category = "browser"` only when `Webwright.Enabled` is true. Default is disabled because browser automation is high-risk.
  - Operators still allowlist browser tools explicitly via `ToolVisibilityContext`; enabling the service does not imply every agent can use it.
  - `task` size cap (4 KB) prevents prompt smuggling at the boundary.
  - `start_url` validated as absolute http/https before forwarding (reuse `web_fetch` URL guards).
- **Sidecar side:**
  - Domain allowlist / denylist enforced by sidecar-controlled validation and Playwright route interception, **not** trusted to Webwright alone.
  - Egress policy applies to top-level navigation, redirects, subresources, downloads, WebSockets, and generated-script `fetch()` calls.
  - DNS resolution is checked before navigation and after redirects; private, loopback, link-local, multicast, and metadata-service IP ranges are blocked to mitigate SSRF and DNS rebinding.
  - Disallowed protocols: `file:`, `ftp:`, `data:` outside inline asset capture.
  - Subprocess wall-clock cap (default 10 min) hard-kills the run.
  - Artifact bytes per run capped; oldest artifacts evicted on overflow.
  - Secret redaction pass on `final_script_log.txt` before exposure (Bearer/api-key patterns).
  - Artifact TTL sweep (default 24h).
  - Container runs non-root, unprivileged, with dropped Linux capabilities where Playwright supports it, plus CPU, memory, pids, temp, and output-volume quotas.
- **Network/auth:** static `Bearer` token, env-injected.

## Configuration
Add `WebwrightConfig` to `LeanKernel.Abstractions/Configuration/` and reference from `LeanKernelConfig`.

```json
"LeanKernel": {
  "Webwright": {
    "Enabled": false,
    "BaseUrl": "http://webwright:8000",
    "ApiToken": "",
    "RequestTimeoutSeconds": 15,
    "MaxArtifactBytes": 2000000,
    "MaxOutputChars": 12000,
    "DefaultModel": "gpt-4o",
    "HealthProbe": { "Enabled": true }
  }
}
```

Browser-service env vars:
- `API_TOKEN` — must match LeanKernel's `Webwright:ApiToken`.
- `LITELLM_BASE_URL` — e.g. `http://litellm:4000`.
- `LITELLM_API_KEY` — dedicated LiteLLM virtual key for webwright with its own budget, model allowlist, tags, and metadata; do not use the LiteLLM master key.
- `WEBWRIGHT_MODEL` — default model alias.
- `MAX_CONCURRENT_RUNS` (default 2).
- `MAX_QUEUE_DEPTH` (default 20).
- `RUN_WALL_CLOCK_SECONDS` (default 600).
- `ARTIFACT_TTL_HOURS` (default 24).
- `DOMAIN_ALLOWLIST` / `DOMAIN_DENYLIST` (comma-separated; allowlist empty = allow-all, set to non-empty to enforce).

## docker-compose integration
Add to `docker-compose.yml`:

```yaml
services:
  webwright:
    build: ./services/webwright
    environment:
      - API_TOKEN=${WEBWRIGHT_API_TOKEN}
      - LITELLM_BASE_URL=http://litellm:4000
      - LITELLM_API_KEY=${WEBWRIGHT_LITELLM_KEY}
      - WEBWRIGHT_MODEL=gpt-4o
      - MAX_CONCURRENT_RUNS=2
      - MAX_QUEUE_DEPTH=20
      - RUN_WALL_CLOCK_SECONDS=600
      - ARTIFACT_TTL_HOURS=24
    volumes:
      - browserdata:/app/outputs
    depends_on:
      - litellm
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:8000/health"]
      interval: 30s
      timeout: 5s
      retries: 3

volumes:
  browserdata:
```

The `engine` service gains `depends_on: webwright` and env:
- `LEANKERNEL__WEBWRIGHT__ENABLED=true`
- `LEANKERNEL__WEBWRIGHT__BASEURL=http://webwright:8000`
- `LEANKERNEL__WEBWRIGHT__APITOKEN=${WEBWRIGHT_API_TOKEN}`

## Implementation Plan
1. Add `WebwrightConfig` and bind it in `LeanKernelConfig`.
2. Add typed sidecar client + DTOs in `LeanKernel.Tools/BuiltIn/Browser/`:
   - `IWebwrightClient`, `WebwrightClient`,
   - request/response models, error mapper.
3. Add four tool definitions (`BrowserRunTaskTool`, `BrowserGetRunTool`, `BrowserGetArtifactTool`, `BrowserCancelRunTool`).
4. Register tools conditionally in `ToolsServiceCollectionExtensions.AddLeanKernelTools` only when `WebwrightConfig.Enabled` is true; register named `HttpClient` (Bearer + base URL) and `WebwrightHealthProbe` in `LeanKernelHardeningServiceCollectionExtensions`.
5. Build `services/webwright/`:
   - `Dockerfile` based on `mcr.microsoft.com/playwright/python:v1.49.1-jammy`,
   - `pip install webwright==<pin> fastapi uvicorn`,
   - `playwright install firefox`,
   - `app/main.py` — FastAPI endpoints, run manager, artifact server, retention sweep,
   - `config/leankernel.yaml` — Webwright overlay pointing model backend at LiteLLM,
   - smoke script for `/health`.
6. Update `docker-compose.yml` and `.env.example`.
7. Tests:
   - **Unit** (`test/LeanKernel.Tests.Unit/Tools/Browser/`): validation, JSON output mapping, `browser_cancel_run` triggers `DELETE`, best-effort submit cancellation, error envelope mapping.
   - **Integration** (`test/LeanKernel.Tests.Integration/`): tool ↔ stub HTTP server.
   - **End-to-end** (`test/LeanKernel.Tests.Playwright/`): real `webwright` container against a local fixture HTML page using a stub LiteLLM that returns a canned script.
8. Update docs: `README.md`, new `docs/features/browser-tool.md`, configuration reference, and bump the `docs/plans/index.md` entry to reflect Webwright integration.

## Validation Plan
This validation sequence applies to the future implementation, not to documentation-only PRD edits.

- `dotnet restore src/LeanKernel.sln`
- `dotnet build src/LeanKernel.sln --no-restore -v minimal`
- `dotnet test src/LeanKernel.sln --no-build -v minimal`
- `scripts/quality/test-coverage.sh`
- `scripts/quality/sonarqube-scan.sh`
- `docker compose build`
- `docker compose up -d webwright && curl -fsS http://localhost:8000/health`
- authenticated readiness check against `GET /ready`

## Risks and Mitigations
- **Upstream stability risk:** Webwright is new (May 2026).
  - Pin to a specific version (`webwright==<x.y.z>`); track upstream releases via Dependabot; sidecar API is engine-agnostic so we can swap implementations without changing the LeanKernel tool surface.
- **Long-running runs:** browser tasks can exceed any reasonable HTTP timeout.
  - Async submit-then-poll is the primary mitigation; subprocess wall-clock cap prevents zombies.
- **Cost surprise:** Webwright's loop can make many LLM calls per task.
  - All calls go through LiteLLM with a webwright virtual key budget and model allowlist; `model` param lets callers pick an approved cheaper alias.
- **Navigation abuse:** untrusted sites, exfiltration.
  - Sidecar-enforced domain/IP allow/deny across top-level navigation, redirects, subresources, downloads, WebSockets, and generated-script fetches.
- **Cancellation leaks:** abandoned tool calls leave Firefox processes.
  - `DELETE` → SIGTERM; reaper for orphans past wall-clock.
- **Artifact growth:** screenshots accumulate fast.
  - TTL sweep + per-run byte cap.
- **Artifact traversal:** artifact IDs can otherwise become filesystem paths.
  - Manifest-only opaque IDs, canonical path validation, and run-directory confinement.
- **Webwright loop drift:** future Webwright versions may change workspace layout.
  - Manifest construction lives in the sidecar; if Webwright reshapes outputs, only sidecar updates. Pinned version + integration tests catch breaks.

## Acceptance Criteria
- Four browser tools register and execute through `IToolExecutor`.
- `browser_run_task` against a sandbox HTML fixture produces a completed run with a `final_script.py`, at least one screenshot, and a non-null `finalDatum`.
- All Webwright LLM calls observed in LiteLLM logs under the webwright virtual key (proves routing and budget attribution).
- `browser_cancel_run` propagates from tool to subprocess, and submit-time cancellation attempts best-effort cleanup when a `runId` is known.
- `WebwrightHealthProbe` reports readiness through `/api/health`.
- Domain denylist and private-IP protections block forbidden navigation and subresource fetches (verified via integration test).
- Tests and quality gates pass under repository validation sequence.

## Open Decisions (deferred)
- Webwright version pin — confirm at implementation start.
- Whether to expose `task_showcase.yaml` mode (produces structured `report.json`) as a tool flag — likely useful, defer to v1.1.
- Default `MAX_CONCURRENT_RUNS` — 2 is conservative for an 8 GB container; tune after profiling.
- Whether to import webwright LiteLLM usage into LeanKernel's `ISpendTracker` for unified in-app spend views (v1 enforcement is the LiteLLM virtual-key budget).

## Independent Plan Review Notes
- The non-negotiable v1 choices are: (1) Webwright as engine, (2) async submit-then-poll, (3) Webwright LLM routed through LiteLLM, (4) single-purpose tools.
- Keep the sidecar API engine-agnostic so a future v2 could swap Webwright for an alternate engine without breaking LeanKernel tool contracts.
- Avoid leaking Webwright-specific concepts (`final_script.py`, `task_showcase`) into tool parameter names beyond what's already in the artifact manifest.
- Review update: browser tools must be disabled by default through `Webwright.Enabled`, then explicitly allowed through tool governance for trusted agents/operators.
- Review update: webwright spend is enforced at LiteLLM virtual-key scope; LeanKernel's in-process spend tracker does not automatically account for Webwright's direct LiteLLM calls.
- Review update: artifact access must use manifest-derived opaque IDs and canonical path checks; the API must not accept raw artifact paths.
