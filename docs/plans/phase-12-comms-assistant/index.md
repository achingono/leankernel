# Phase 12 Email And Calendar Assistant

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Deliver the first high-value action-taking capabilities of a personal assistant: managing the user's email and calendar. Building on the connector hub (Phase 11), the assistant can read and triage email, draft and send replies, summarize threads, and read availability, create/reschedule events, and send invites — all person-scoped so the same context is available across channels, and all side-effecting actions gated by the autonomy/approval engine (Phase 14).

## Scope
This phase implements email and calendar feature logic and their tool surfaces on top of connector-provided authorized access. It reuses the turn runtime, memory, and governance. It does not build a general integrations framework (Phase 11) or generic task management (Phase 13), though calendar events and email follow-ups may create tasks/reminders once Phase 13 exists.

## In Scope
- Email capabilities: list/search, read, thread summarization, triage/labeling, draft, reply, and send — with attachment awareness.
- Calendar capabilities: read availability across calendars, create/update/cancel events, propose meeting times, and send/respond to invites.
- Person-scoped context: email/calendar context and derived facts are available on every channel via Phase 10 person scope.
- Proactive surfaces (optional, behind config): daily agenda briefing and important-email summaries delivered via channels/scheduler.
- All write actions (send email, create/modify events, respond to invites) routed through the approval engine and tool governance; reads are lower-risk but still governed.
- Provider-agnostic design with at least one concrete provider (e.g., Google or Microsoft Graph) via the Phase 11 connector.
- Configuration for enabled providers, briefing schedule, and default approval posture; startup validation.
- Tests for triage/summarization correctness, draft/send gating, availability computation, event lifecycle, and cross-channel context.

## Out of Scope
- The connector/OAuth framework itself (Phase 11 dependency).
- Generic task/reminder management (Phase 13), beyond creating follow-ups where that phase exists.
- The autonomy/approval engine internals (Phase 14) — this phase consumes it and fails safe (draft-only) if unavailable.

## Entry Criteria
- Phase 11 connector hub provides authorized, person-scoped access to an email/calendar provider.
- Turn runtime, memory, and tool governance are operational.
- Phase 14 approval engine available or a conservative draft-only fallback is acceptable for writes.

## Exit Criteria
The assistant can triage and summarize a user's email, draft and (with approval) send replies, read availability, and create/reschedule events with invites — consistently across channels and safely gated for all writes. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
