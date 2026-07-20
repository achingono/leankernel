# Phase 16 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Sensitive claims (tokens, SSNs, unbounded custom) persisted/rendered | Privacy/security breach | Strict claim allowlist; never persist tokens; redact | Open |
| R2 | Identity block bloats the prompt / blows budget | Cost/latency, truncation | Bounded field set; budget admission; concise rendering | Open |
| R3 | Stale identity persists after IdP changes | Wrong personalization | Refresh on each login + change detection | Open |
| R4 | Cross-user/tenant identity leakage in prompt | Privacy breach | Strict per-request permit scoping; isolation tests | Open |
| R5 | Prompt-injection via claim values (e.g., display name) | Instruction hijack | Treat claims as data; escape/neutralize; no instruction interpolation | Open |
| R6 | Non-deterministic rendering breaks reproducibility | Flaky tests/output | Stable field ordering; snapshot tests | Open |

## Open Decisions
- Store additional claims as columns on `UserEntity` vs a separate claims/profile entity (JSON).
- Which claims are in the default allowlist and which are rendered vs stored-only.
- Whether identity context is always-on or admitted via the Phase 03 gatekeeper.
| R1 | Sensitive claims (tokens, SSNs, unbounded custom) persisted/rendered | Privacy/security breach | Strict claim allowlist; never persist tokens; redact | Mitigated |
| R2 | Identity block bloats the prompt / blows budget | Cost/latency, truncation | Bounded field set; budget admission; concise rendering | Mitigated |
| R3 | Stale identity persists after IdP changes | Wrong personalization | Refresh on each login + change detection | Mitigated |
| R4 | Cross-user/tenant identity leakage in prompt | Privacy breach | Strict per-request permit scoping; isolation tests | Mitigated |
| R5 | Prompt-injection via claim values (e.g., display name) | Instruction hijack | Treat claims as data; escape/neutralize; no instruction interpolation | Monitoring |
| R6 | Non-deterministic rendering breaks reproducibility | Flaky tests/output | Stable field ordering; snapshot tests | Mitigated |
## Open Decisions
- No open implementation blockers remain for Phase 16. Future tuning can adjust default allowlists and prompt field selection per environment.
