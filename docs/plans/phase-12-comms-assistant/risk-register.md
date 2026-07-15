# Phase 12 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Assistant sends email/invites without consent | Reputational/relational harm | Mandatory approval; draft-only fail-safe | Open |
| R2 | Reply-all / wrong-recipient mistakes | Data leak | Recipient confirmation in approval; conservative defaults | Open |
| R3 | Timezone/availability miscalculation | Double-booking / missed meetings | Explicit tz handling + tests | Open |
| R4 | Summarization leaks content cross-scope | Privacy breach | Strict person scoping; no cross-user aggregation | Open |
| R5 | Provider rate limits / quota exhaustion | Degraded service | Backoff + caching + batch limits | Open |
| R6 | Large mailboxes overwhelm context | Cost/latency | Windowed retrieval + summarization budgets | Open |

## Open Decisions
- First provider (Google vs Microsoft Graph).
- Default briefing cadence and opt-in vs opt-out.
- Whether triage writes labels or only proposes them initially.
