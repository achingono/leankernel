# Phase 16 Activities

## Step-By-Step Activities
1. Produce a duplicate map across Signal terminal, Teams terminal, and Gateway with explicit keep/move decisions.
2. Create a new shared project in `src/Common` for terminal/gateway shared runtime helpers and add project references where needed.
3. Introduce wrappers/adapters first (no behavior change), then switch callers in a second step and remove old duplicates.
4. Move reusable helpers into the shared project with neutral naming and API shape.
5. Refactor Signal and Teams terminals to call shared helpers instead of local duplicates.
6. Refactor Gateway to use shared helpers where overlap exists.
7. Update solution/project references and container build paths if required.
8. Run verification: project builds, targeted tests, compose smoke checks.
9. Run quality/deep-review workflow and capture evidence.

## Review Focus
- No behavior regressions in message parsing or forwarding.
- Health endpoint JSON schema remains stable.
- Shared code has no channel-specific leakage.
- DI/setup changes remain minimal and explicit.
