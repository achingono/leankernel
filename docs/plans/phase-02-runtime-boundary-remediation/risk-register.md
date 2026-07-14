# Phase 02 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Tightening `/v1/*` access from public to authenticated may break expected callers or test fixtures. | Production integration break or rollout friction. | Confirm intended access model up front, stage config/test updates together, and document any migration path. | Open |
| R2 | Forwarded-header hardening may behave differently across local dev, test, and reverse-proxy production environments. | Requests may resolve the wrong tenant or fail unexpectedly after rollout. | Validate trusted proxy configuration per environment and add integration coverage for forwarded-host handling. | Open |
| R3 | Tenant-scoping guest users may require data backfill or guest identity remapping if anonymous rows already exist. | Historical anonymous data may no longer resolve cleanly. | Audit existing guest rows first and define a one-time migration strategy before changing uniqueness rules. | Open |
| R4 | Replay protection can suppress legitimate repeated actions if the idempotency key is defined too broadly. | Lost intended writes or confusing client behavior. | Scope idempotency to a single logical request operation and persist enough metadata to distinguish real retries from new requests. | Open |
| R5 | Adding transcript compaction can change model behavior if summaries omit details needed later. | Regression in conversation quality or loss of important context. | Keep a small recent-turn window plus tested summary generation and verify representative long-session scenarios. | Open |
| R6 | Correct concurrency handling for agent state may surface real conflicts that were previously hidden. | Increased retry behavior or visible save failures under parallel use. | Add deterministic conflict handling, targeted concurrency tests, and clear operational logs. | Open |
| R7 | EF relationship cleanup may require schema/data migration work beyond a simple model fix. | Migration failure or orphaned relationship data in upgraded environments. | Inspect current migrated databases before applying the cleanup migration and verify both empty and upgraded paths. | Open |
| R8 | Enabling real JWT validation (finding C4) will reject tokens that current clients/tests send unsigned or with wrong issuer/audience. | Existing integration clients and fixtures break on rollout. | Confirm the signing/issuer/audience source, update test fixtures to mint valid tokens, and stage the config change with the `/v1/*` access decision (R1). | Open |
| R9 | Forcing anonymous session materialization (finding M6) changes cookie/session behavior and may invalidate in-flight anonymous sessions. | Anonymous users lose continuity once at rollout; existing ephemeral guest rows remain orphaned. | Roll out with a session-write marker, plan a one-time cleanup of orphaned guest/session rows, and verify repeat-request continuity. | Open |
| R10 | Making identity resolution async (finding M7) touches the `IPermit` contract and every consumer. | Broad refactor risk across Gateway/Logic call sites. | Prefer resolving once in async middleware into `HttpContext.Items` so `IPermit` members stay synchronous readers; add targeted tests before changing the interface. | Open |

## Open Decisions
- Should `/v1/responses` and `/v1/conversations` require authenticated access, or remain anonymously callable under explicit quotas and abuse controls?
- What request field or header is the canonical idempotency key for transcript and memory persistence?
- What transcript compaction trigger should be used: turn count, token estimate, age, or a hybrid threshold?
- Does the memory scope contract remain namespace-based, or should `IMemoryClient` be simplified so Logic cannot omit scope by accident again?
