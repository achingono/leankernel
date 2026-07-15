# Phase 11 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Token vault compromised or tokens logged | Account takeover | Encrypt at rest; never log tokens; least-privilege scopes | Open |
| R2 | Credentials leak across persons/tenants | Privacy breach | Strict person+tenant scoping; isolation tests | Open |
| R3 | OAuth CSRF / code injection | Account hijack | Enforce state + PKCE; validate redirect URIs | Open |
| R4 | Over-broad scopes requested | Excess exposure | Request minimal scopes; incremental consent | Open |
| R5 | Connector write actions run without approval | Unwanted side effects | Gate writes conservatively until Phase 14 approval engine | Open |
| R6 | Upstream revocation not detected | Stale/failing calls | Detect 401/invalid_grant; prompt re-consent | Open |

## Open Decisions
- Encryption mechanism (data-protection API, KMS, envelope encryption).
- First reference provider (Google vs Microsoft Graph vs GitHub).
- Where the OAuth redirect endpoint lives relative to channels.
