# Phase 16 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Sensitive claims (tokens, SSNs, unbounded custom) persisted/rendered | Privacy/security breach | Strict claim allowlist; never persist tokens; redact | Closed |
| R2 | Identity block bloats the prompt / blows budget | Cost/latency, truncation | Bounded field set; budget admission; concise rendering; `MaxPromptTokens` cap | Closed |
| R3 | Stale identity persists after IdP changes | Wrong personalization | Refresh on each login + change detection | Closed |
| R4 | Cross-user/tenant identity leakage in prompt | Privacy breach | Strict per-request permit scoping; isolation tests | Closed |
| R5 | Prompt-injection via claim values (e.g., display name) | Instruction hijack | Treat claims as data in backtick code blocks; no instruction interpolation | Closed |
| R6 | Non-deterministic rendering breaks reproducibility | Flaky tests/output | Stable field ordering via `OrderBy`; deterministic field enumeration order | Closed |

## Decisions
- **Claims storage**: JSON-in-column (`RolesJson`, `GroupsJson`, `CustomClaimsJson` text columns on `UserEntity`) rather than child entities. Rationale: read-all/write-all pattern, bounded cardinality, no query-by-claim requirement.
- **Default allowlist**: All standard OIDC/OAuth fields (name, email, preferred_username, locale, timezone, org, roles, groups). Custom claims are deny-by-default via `AllowedCustomClaims: []`.
- **Rendered fields**: Controlled by `PromptFields` config array. Default includes all standard fields plus custom_claims.
- **Gatekeeper admission**: Identity context is always admitted under system budget ahead of memory/retrieval items.
