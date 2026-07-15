# Phase 12 Activities

## Step-By-Step Activities
1. Define provider-agnostic email and calendar service contracts (list/search/read/summarize/draft/send; availability/create/update/cancel/respond) over the Phase 11 connector.
2. Implement the concrete provider adapter (e.g., Google or Microsoft Graph) behind those contracts using authorized, person-scoped credentials.
3. Implement email capabilities: search/list, thread read + summarization, triage/labeling, and draft/reply/send with attachment awareness.
4. Implement calendar capabilities: multi-calendar availability computation, event create/update/cancel, meeting-time proposals, and invite send/response.
5. Expose these as governed tools; route every write (send email, modify events, respond to invites) through the approval engine, with a draft-only fail-safe when it is unavailable.
6. Surface email/calendar context and derived facts to person-scoped memory so they are available across channels.
7. Implement optional proactive briefings (daily agenda, important-email summary) via the scheduler + channels, behind configuration.
8. Add configuration (enabled providers, briefing schedule, default approval posture) and startup validation.
9. Add tests: triage/summarization behavior, draft/send gating, availability correctness, event lifecycle, invite handling, and cross-channel context availability.
10. Document email and calendar features in `docs/features/`.

## Review Focus
- No email is sent and no event is modified without approval (fail-safe to draft-only).
- Summaries/triage do not exfiltrate content beyond the user's own scope.
- Availability and timezone handling are correct.
- Derived facts respect person scope and tenant isolation.
- Provider adapter is swappable behind the service contracts.
