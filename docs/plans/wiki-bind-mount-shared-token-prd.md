# PRD: Bind-Mounted Wiki Storage with Shared Auth Token Volume

## Date
2026-05-28

## Request
Store wiki pages in bind-mounted `./data/wiki`, while keeping the engine/GBrain auth token in the shared volume used previously.

## Problem Statement
Current compose/runtime wiring has two issues:
1. `data/wiki` is referenced like a named volume but not declared, causing compose validation failures.
2. GBrain writes `.engine-token` to `/app/data/wiki/.engine-token`, while engine reads from `/app/data/gbrain/.engine-token`, creating auth fallback mismatch.

## Goals
- Persist wiki pages in host bind mount `./data/wiki`.
- Preserve shared auth token exchange via named volume mounted at `/app/data/gbrain`.
- Keep existing kernel token resolution logic intact.

## Non-Goals
- Changing GBrain auth model or disabling bearer auth.
- Refactoring kernel knowledge client behavior beyond path alignment.

## Implementation Plan
1. Update `docker-compose.yml` wiki mounts to bind mounts:
   - `./data/wiki:/app/data/wiki` on `gbrain`.
   - `./data/wiki:/app/data/wiki:ro` on `engine`.
2. Add/restore named shared volume mount for auth token:
   - `gbrain-data:/app/data/gbrain` on `gbrain`.
   - `gbrain-data:/app/data/gbrain:ro` on `engine`.
3. Update `config/gbrain/start-gbrain.sh` token file path to `/app/data/gbrain/.engine-token` and ensure directory exists before writing.
4. Leave `src/LeanKernel.Knowledge/GBrainAuthHandler.cs` unchanged so fallback remains `/app/data/gbrain/.engine-token`.
5. Validate with compose/build/test/quality commands.

## Independent Plan Review (Subagent)
Status: Approved with minor clarifications.

Review highlights:
- Keep token path fully consistent across all producers/consumers.
- Ensure wiki and gbrain mounts are distinct and do not overlap.
- Keep engine mounts read-only where possible.
- Verify startup behavior when token is not immediately available.
- Update docs if needed for mount and token path expectations.

## Risks and Mitigations
- Risk: token unreadable due to path or permissions.
  - Mitigation: mount `gbrain-data` into both services and validate token existence/read access.
- Risk: compose regression from invalid volume syntax.
  - Mitigation: use explicit bind mount `./data/wiki` and run `docker compose config`.
- Risk: startup race where token not yet created.
  - Mitigation: rely on existing retry flows; verify runtime logs for graceful behavior.

## Validation Checklist
- `docker compose config` succeeds.
- `docker compose up engine -d --build` succeeds.
- Engine reads token from `/app/data/gbrain/.engine-token` when `LEANKERNEL__GBRAIN__AUTHTOKEN` is empty.
- Wiki content persists to host `./data/wiki`.
- `dotnet build` and `dotnet test` run.
- `scripts/quality/sonarqube-scan.sh` run and results captured.
