# Phase 10 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Incorrect linking merges two different people's memory | Severe privacy breach | Mandatory verification (one-time code); no auto-merge on weak signals | Open |
| R2 | Person layer weakens tenant isolation | Cross-tenant leak | Tenant stays top boundary; forbid cross-tenant linking; isolation tests | Open |
| R3 | Migration loses or duplicates memory pages | Data loss | Dry-run + backup + reversible + deterministic dedupe | Open |
| R4 | Dropping channelId breaks per-channel conversation continuity | UX regression | Keep history/session dims configurable; memory-only scope change | Open |
| R5 | Anonymous/guest identities accidentally share memory | Privacy breach | Guests stay session-isolated; only verified links merge | Open |
| R6 | Double-prefixing memory keys after scope refactor | Broken memory reads | Preserve scope-relative key contract; regression tests | Open |

## Open Decisions
- Person model: new `PersonEntity` vs promote `UserEntity` (affects migration surface).
- Whether transcript history is shared cross-channel by default or opt-in.
- Verification channel(s) accepted for linking (email, SMS/Signal code, OIDC step-up).
