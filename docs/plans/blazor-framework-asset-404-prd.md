# PRD: Resolve Blazor Framework Script 404 in Gateway

## Context
The Gateway UI renders HTML successfully, but interactive bootstrapping fails because the browser receives `404 Not Found` for:

- `GET /_framework/blazor.web.js`

Observed behavior confirms:
- `GET /` returns `200 OK`
- `GET /_framework/blazor.web.js` returns `404 Not Found`

## Goal
Restore successful delivery of Blazor framework assets so the UI hydrates and interactive components function correctly.

## Independent Plan Review
Reviewed with a different model (`GPT-4.1 (copilot)`) before implementation.

Reviewer highlights:
- Validate middleware ordering before endpoint mappings.
- Add explicit static asset endpoint mapping for framework assets.
- Confirm script choice remains correct for render mode.
- Include rollback path and post-change verification.

## Reviewed Implementation Plan
1. Review and preserve correct middleware/endpoint order in `Program.cs`.
2. Add static asset endpoint mapping (`MapStaticAssets`) so `/_framework/*` assets are served.
3. Keep `blazor.web.js` for interactive server mode unless runtime evidence indicates otherwise.
4. Normalize script URL to an absolute app-root path in `App.razor` to avoid route-relative pitfalls.
5. Rebuild/restart the engine container.
6. Verify:
   - `GET /` is `200`
   - `GET /_framework/blazor.web.js` is `200` with JavaScript content type.
7. Run targeted quality checks (build/tests available in environment); if they fail due to environment limitations, report clearly.

## Constraints
- Keep changes minimal and feature-local to `LeanKernel.Gateway`.
- Avoid altering unrelated routing or API behavior.
- Preserve current render mode (`InteractiveServer`).

## Rollback
If regressions appear, revert:
- `Program.cs` static asset mapping change.
- `App.razor` script path normalization.

Then revalidate previous behavior and inspect alternative causes (path base/reverse proxy rewrite rules).

## Acceptance Criteria
- Browser no longer reports 404 for `/_framework/blazor.web.js`.
- Blazor UI hydrates without framework script load errors.
- Root page and existing gateway endpoints remain operational.
