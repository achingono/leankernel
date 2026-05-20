# Container Startup Skill Initialization PRD

## Context
The engine container process stays alive but never binds the HTTP port. Docker health checks fail and mark the container unhealthy. Runtime logs consistently stop after BinaryResolver manifest load, indicating startup is blocked during hosted service initialization.

## Root Cause Hypothesis
SkillHostedService currently performs full skill initialization synchronously in StartAsync. If initialization hangs or stalls, ASP.NET host startup does not complete, so Kestrel never starts listening.

## Implementation Plan
1. Make SkillHostedService startup non-blocking.
- Start skill initialization in a tracked background task from StartAsync.
- Return from StartAsync promptly so web host can bind and health endpoint becomes reachable.

2. Add startup safety guards.
- Track readiness with a boolean state.
- Skip watcher-triggered refreshes until initial skill load completes.
- Serialize refresh operations with a lock to avoid overlapping refreshes.

3. Improve observability.
- Add phase logs around registry initialization, plugin initialization, and listener notifications.
- Log degraded startup when skills are still initializing.

4. Harden shutdown behavior.
- Track initialization CTS/task.
- Cancel on StopAsync and wait briefly; do not block shutdown indefinitely.

5. Add tests.
- Verify StartAsync returns quickly even when initialization is intentionally delayed.
- Verify refresh is skipped before initial readiness and allowed after readiness.

6. Validate quality gates.
- dotnet build src/LeanKernel.sln --no-restore -v minimal
- dotnet test src/LeanKernel.sln --no-build -v minimal
- scripts/quality/sonarqube-scan.sh

## Independent Plan Review
Reviewed by a separate Explore subagent.

Reviewer findings incorporated:
- Risk of refresh/init race addressed with readiness gate and lock.
- Risk of shutdown hang addressed with bounded wait in StopAsync.
- Missing diagnostics addressed with explicit phase logging.
- Tests added for startup latency and refresh gating behavior.

## Acceptance Criteria
- Engine container reaches listening state and health endpoint is reachable.
- Engine container is healthy in docker compose.
- Skill initialization no longer blocks host startup.
- Unit tests for startup non-blocking behavior and refresh gating pass.
