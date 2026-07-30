# Phase 24 Exit Criteria

## Gate Checklist
- [ ] `Identity:AllowGuestFallback` defaults to `false` in `IdentitySettings`.
- [ ] `appsettings.Development.json` sets `AllowGuestFallback: true`.
- [ ] Path C returns 401 when `AllowGuestFallback` is `false`.
- [ ] Path C resolves guest user when `AllowGuestFallback` is `true`.
- [ ] A structured warning is logged when Path C is entered, and separately when it is blocked.
- [ ] Full project builds and all tests pass (including updated existing tests).
- [ ] Configuration schema change documented in `docs/configuration/index.md`.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | Coding Agent | Pending | |
| Reviewer | Model review | Pending | |
| Approver | Repository owner | Pending | |
