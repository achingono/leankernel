# LiteLLM and GBrain Proxy Wiring PRD

- **Status:** Draft
- **Goal:** Route LeanKernel's LiteLLM and GBrain runtime configuration through repo-root environment variables, and point GBrain at the LiteLLM proxy instead of direct provider endpoints.
- **Scope:** Compose/runtime wiring, env examples, and the docs that describe those settings.

## Problem statement

The current development compose file hard-codes only a small subset of LiteLLM-related variables, while GBrain still advertises direct embedding/chat provider keys in the repository docs. The result is inconsistent local setup: the proxy container does not clearly receive the provider envs declared in the repo, and the GBrain runtime contract is documented as if it still talks to providers directly instead of through LiteLLM.

## Plan

1. Update `docker-compose.yml` so the LiteLLM service receives the repo-root provider variables it needs, and so GBrain is configured to call the LiteLLM proxy with proxy-specific env vars.
2. Expand `.env.example` with the LiteLLM proxy settings and the provider variables that belong to the proxy container, so the repo root becomes the single source of truth for local setup.
3. Update the infrastructure docs to describe the proxy-based flow and the expected env surface.
4. Keep the existing GBrain HTTP base URL shape intact unless validation shows a mismatch in the current runtime contract.

## Non-goals

- Changing LeanKernel's `IKnowledgeService` or GBrain MCP transport shape.
- Reworking GBrain model selection logic beyond the environment wiring needed for the proxy.
- Modifying unrelated service images or runtime behavior.

## Validation

1. Inspect the compose render with `docker compose config`.
2. Check the changed files for consistency after the edit.
3. If Docker or .NET tooling is available, run the narrowest relevant build/test check for the touched surface.

## Acceptance criteria

- LiteLLM receives its provider env vars from the repo-root configuration surface instead of relying on ad hoc container values.
- GBrain is configured to use LiteLLM proxy settings rather than direct provider API endpoints.
- The repo docs and env examples match the runtime contract.