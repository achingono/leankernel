# Phase 10 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Incorrect linking merges two different people's memory | Severe privacy breach | Mandatory verification (one-time code); no auto-merge on weak signals | Open |
| R2 | Person layer weakens tenant isolation | Cross-tenant leak | Tenant stays top boundary; forbid cross-tenant linking; isolation tests | Open |
| R3 | Migration loses or duplicates memory pages | Data loss | Dry-run + backup + reversible + deterministic dedupe; channelId retained | Open |
| R4 | Dropping channelId breaks per-channel conversation continuity | UX regression | ChannelId retained as a governed dimension; history/session dims stay configurable; memory-only scope change | Open |
| R5 | Anonymous/guest identities accidentally share memory | Privacy breach | Guests stay session-isolated; only verified links merge | Open |
| R6 | Double-prefixing memory keys after scope refactor | Broken memory reads | Preserve scope-relative key contract; regression tests | Open |
| R7 | Wildcard-default policy shares memory across channels a user expected to be private | Privacy surprise | Document default clearly; make isolation easy; directional AND requires both sides to opt in; provide per-channel isolation config | Open |
| R8 | Policy read fan-out across many channels degrades performance | Latency/cost | Bound the effective channel set; cache policy resolution; de-duplicate results | Open |
| R9 | Read/write scopes diverge (write to one channel, read set excludes it) | Lost memory | Always include the current channel in its own read set; contract tests | Open |
| R10 | Shared channels accumulate divergent/conflicting 5W1H facts about the same entity | Inconsistent, untrustworthy memory | Cross-channel supersession/merge over the mutually visible set; deterministic conflict resolution; reconciliation pass on policy change | Open |
| R11 | Reconciliation merges a fact into a channel that should not see it | Privacy/visibility breach | Merge only within the mutually visible set (directional AND); provenance retained; visibility tests | Open |
| R12 | Deterministic conflict resolution silently discards a genuinely conflicting fact | Data/semantic loss | Flag irreconcilable conflicts instead of overwriting; keep superseded pages retired-not-deleted; reversible reconciliation | Open |
| R13 | Asymmetric sharing merged/written back across the one-way boundary | Leak into shared-from channel; broken isolation | Never merge/supersede across non-mutual boundary; non-destructive read-time overlay only; writes confined to writer's own scope; revocation leaves no residual state | Open |

## Open Decisions
- Person model: new `PersonEntity` vs promote `UserEntity` (affects migration surface).
- Whether transcript history is shared cross-channel by default or opt-in.
- Verification channel(s) accepted for linking (email, SMS/Signal code, OIDC step-up).
- Whether the sharing policy is configured per-tenant default, per-person override, or both (schema owned by Phase 06).
- Conflict-resolution strategy for reconciliation (newest-wins default vs explicit-supersession-only vs manual review for flagged conflicts).
