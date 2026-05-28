# Browser Automation Tool

LeanKernel can expose an optional browser automation tool family backed by a Webwright sidecar service. The engine keeps browser automation out of the .NET process and talks to the sidecar over authenticated HTTP.

## Tools

Browser tools register only when `LeanKernel:BrowserService:Enabled` is `true`. They still participate in normal tool governance, so trusted callers should allow the `browser` category or specific tool names explicitly.

| Tool | Purpose |
| --- | --- |
| `browser_run_task` | Submits an asynchronous natural-language browser task and returns a `runId`. |
| `browser_get_run` | Polls run status, final datum, errors, and the artifact manifest. |
| `browser_get_artifact` | Fetches one manifest-listed artifact as base64. |
| `browser_cancel_run` | Requests idempotent cancellation of a queued or running browser task. |

`browser_run_task` does not wait for Webwright to finish. Callers submit, then poll `browser_get_run` until the status is `succeeded`, `failed`, `cancelled`, or `timed_out`.

## Sidecar service

The sidecar lives under `config/browser-service/` and is built by Docker Compose as `browser-service`. It provides:

- `GET /health` unauthenticated liveness for Compose.
- `GET /ready` authenticated readiness for LeanKernel provider health tracking.
- `POST /runs`, `GET /runs/{runId}`, `GET /runs/{runId}/artifacts/{artifactId}`, and `DELETE /runs/{runId}` for operational browser runs.

Webwright model calls route through LiteLLM using `LITELLM_BASE_URL` and `LITELLM_API_KEY` inside the sidecar container. Use a dedicated LiteLLM virtual key for `BROWSER_SERVICE_LITELLM_KEY` so browser spend can have its own budget and model allowlist.

## Security defaults

Browser automation is disabled by default. To enable it, set a random `BROWSER_SERVICE_API_TOKEN`, provide a scoped `BROWSER_SERVICE_LITELLM_KEY`, then set `BROWSER_SERVICE_ENABLED=true`.

The sidecar validates task size, start URLs, bearer auth, queue limits, domain allow/deny policy, private IP ranges, and artifact confinement. Artifacts are available only through opaque IDs from the run manifest; callers cannot pass raw file paths.

## Configuration

```json
"LeanKernel": {
  "BrowserService": {
    "Enabled": false,
    "BaseUrl": "http://browser-service:8000",
    "ApiToken": "",
    "RequestTimeoutSeconds": 15,
    "MaxArtifactBytes": 2000000,
    "MaxOutputChars": 12000,
    "DefaultModel": "gpt-4o",
    "HealthProbe": { "Enabled": true }
  }
}
```

Compose environment variables:

| Variable | Purpose |
| --- | --- |
| `BROWSER_SERVICE_ENABLED` | Enables LeanKernel browser tools when `true`. |
| `BROWSER_SERVICE_API_TOKEN` | Shared bearer token for engine-to-sidecar calls. Leave blank until configured. |
| `BROWSER_SERVICE_LITELLM_KEY` | Dedicated LiteLLM virtual key used by Webwright. |
| `BROWSER_SERVICE_MODEL` | Default Webwright model alias. |
| `BROWSER_SERVICE_DOMAIN_ALLOWLIST` / `BROWSER_SERVICE_DOMAIN_DENYLIST` | Optional comma-separated host policies. |

## Related documentation

- [Tool Governance](tool-governance.md)
- [Production Operations](production-ops.md)
- [Phase 3 Configuration](../configuration/phase-3-config.md)
