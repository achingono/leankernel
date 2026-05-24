# Engine Health Recovery PRD

## Context
- Reported issue: `engine` container health stays `unhealthy` and `/api/health` returns `503` even after `litellm` and `gbrain` show healthy in compose.
- Observed provider failures from engine: LiteLLM timeout and GBrain connection refused.
- Root cause evidence: `gbrain serve` defaults to `Bind: 127.0.0.1`, which prevents sibling containers from reaching it on the compose network.

## Goals
- Make engine provider probes recover to healthy when dependencies are actually healthy.
- Keep compose startup robust with no host-network workaround.
- Preserve existing auth behavior for LiteLLM health probes.

## Non-Goals
- Refactoring provider health architecture.
- Broad hardening changes unrelated to current health failure.

## Implementation Plan
1. Update GBrain startup script to bind service to all interfaces (`0.0.0.0`) using supported CLI flag.
2. Keep engine->GBrain URL as compose service DNS (`http://gbrain:8789`) and validate in-container reachability.
3. Rebuild and restart affected services (`gbrain`, `engine`) with compose.
4. Validate health end-to-end:
   - `docker compose ps`
   - `curl -i http://localhost:5080/api/health`
   - inspect engine logs for provider transition to healthy.
5. Run required quality checks for the change scope (build/test and Sonar workflow per repo process).

## Plan Review (Different Model)
Reviewer: Explore subagent using model `GPT-4.1 (copilot)`.

Review outcomes:
- High risk identified: GBrain binding on localhost blocks container-to-container access.
- Recommended approach: explicitly bind GBrain to `0.0.0.0` (preferred over host routing).
- Additional validation requested: connectivity test from engine container to GBrain health endpoint; verify provider transitions.

Incorporated adjustments:
- Added explicit bind step to implementation plan.
- Added in-container connectivity and transition-focused validation.

## Acceptance Criteria
- GBrain listens on `0.0.0.0` in container startup logs.
- Engine can reach `http://gbrain:8789/health` from container network.
- `/api/health` returns `200` with providers healthy once dependencies are up.
- Engine healthcheck status transitions to healthy in Docker.

## Rollback
- Revert startup script bind change and restart compose stack.
- Return to prior behavior if regression is detected.
