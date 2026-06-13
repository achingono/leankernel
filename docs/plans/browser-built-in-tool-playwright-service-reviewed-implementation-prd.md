# Browser Built-in Tool Reviewed Implementation PRD

## Summary
Implement `browser-built-in-tool-playwright-service-prd.md` with a disabled-by-default LeanKernel browser tool family backed by a Webwright sidecar. This reviewed implementation plan incorporates independent review notes before code changes.

## Reviewed Implementation Plan
1. Add `WebwrightConfig` under `LeanKernel.Abstractions/Configuration`, wire it into `LeanKernelConfig`, `ProviderNames`, `appsettings`, `.env.example`, and Docker Compose.
2. Add `IWebwrightClient` under `LeanKernel.Abstractions/Interfaces` and implement the typed client, DTOs, error mapping, validation, and four tools under `LeanKernel.Tools/BuiltIn/Browser`.
3. Register browser tools conditionally in `ToolsServiceCollectionExtensions.AddLeanKernelTools` only when `LeanKernel:Webwright:Enabled` is true.
4. Register the named browser `HttpClient` and `WebwrightHealthProbe` conditionally in `LeanKernelHardeningServiceCollectionExtensions`, using the existing correlation handler chain and authenticated `/ready`.
5. Build the sidecar under `config/webwright/` with FastAPI, Webwright pinned to `0.0.7`, Playwright Firefox, async queue/concurrency, idempotency metadata, subprocess lifecycle, manifest-gated artifact access, bearer auth, `/health`, and `/ready`.
6. Update Compose so `webwright` health checks call unauthenticated `/health`, operational calls require the blank-by-default `WEBWRIGHT_API_TOKEN`, and Webwright routes model calls through LiteLLM with a dedicated browser key.
7. Enforce task length, URL validation, auth, artifact confinement, and artifact ID ownership in both LeanKernel and the sidecar.
8. Add unit and integration coverage for registration gating, validation, client/error mapping, cancellation, and sidecar API behavior.
9. Update README and feature/configuration docs for browser tools, sidecar deployment, security defaults, and validation.
10. Validate with the repository restore/build/test sequence, coverage, Sonar, Docker Compose build, and webwright health checks where available.

## Review Corrections Applied
- Use `config/webwright/` as the sidecar build context to match repository sidecar conventions.
- Keep browser tool registration in `LeanKernel.Tools`; keep HTTP client and health probe registration in `LeanKernel.Gateway` hardening composition.
- Place `IWebwrightClient` in Abstractions so the contract is not hidden in the tool implementation layer.
- Leave browser service tokens blank in `.env.example`; document that operators must provide random secrets.
- Use unauthenticated `/health` for container liveness and authenticated `/ready` for provider readiness.
- Use opaque per-run artifact IDs and verify each artifact ID belongs to the requested run.
- Enforce the 4 KiB task cap in both the C# tool boundary and Python sidecar.
- Reject `wait_seconds` in v1 because long-polling remains deferred.
- Treat cancellation as idempotent: cancelling terminal runs returns the current run status.

## Security Requirements
- Browser automation remains disabled by default via `LeanKernel:Webwright:Enabled=false`.
- Sidecar operational endpoints require static bearer auth; `/health` is the only unauthenticated endpoint.
- `start_url` must be absolute `http` or `https`; private, loopback, link-local, multicast, unspecified, and metadata-service IP targets must be blocked.
- Sidecar egress controls must apply to top-level navigations, redirects, subresources, downloads, WebSockets, and generated-script fetches where Webwright/Playwright integration allows.
- Artifact access must be manifest-only, canonical-path confined, non-symlink escaping, and per-run scoped.
- Logs exposed as artifacts must be redacted for common bearer/API-key patterns.

## Implementation Assumptions
- Webwright is pinned to PyPI `webwright==0.0.7`, the latest available package at implementation start.
- The sidecar image documents the package pin and expected source, but full hash pinning is deferred unless package hashes are available in the build environment.
- Compose uses a dedicated `WEBWRIGHT_LITELLM_KEY`; local developers may set it to a scoped LiteLLM virtual key.
