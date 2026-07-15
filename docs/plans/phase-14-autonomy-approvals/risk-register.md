# Phase 14 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | A side-effecting action bypasses the enforcement point | Unauthorized action | Single choke point; deny-by-default; no-bypass tests | Open |
| R2 | Expired/unanswered approvals default to allow | Unwanted actions | Default-deny on timeout; explicit tests | Open |
| R3 | Audit log incomplete or mutable | No accountability | Append-only, hash-chained; write-before-execute ordering | Open |
| R4 | Over-permissive default policy | Excess autonomy | Least-autonomy defaults; explicit opt-in to raise | Open |
| R5 | Approval prompt spoofing/replay | Social-engineering bypass | Signed, single-use, expiring approval tokens | Open |
| R6 | Approval fatigue leads users to blanket-approve | Effective loss of control | Sensible auto-approve for low-risk; batching; clear summaries | Open |

## Open Decisions
- Audit store: reuse Phase 08 diagnostics persistence vs a dedicated append-only store.
- Default autonomy matrix per action class.
- Approval token format and channel-specific confirmation UX.
