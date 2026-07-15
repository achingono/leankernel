# Phase 13 Task And Reminder Management

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Give the assistant durable task, reminder, and follow-up management so it can capture commitments from any conversation, track them to completion, and proactively remind the user on the right channel at the right time. Tasks are person-scoped (Phase 10) so they are visible and actionable across every channel, executed via the scheduler (Phase 07) and delivered via channels (Phase 06).

## Scope
This phase covers the task/reminder domain model, capture (explicit and inferred), lifecycle, due/recurrence handling, and proactive notification. It reuses the scheduler for time-based firing and channels for delivery. It does not build calendar-event management (Phase 12) or a generic automation/recipe engine, though it may be created from email/calendar follow-ups.

## In Scope
- A person-scoped task/reminder model: title, notes, due time, recurrence, status, source (conversation/email/manual), and priority.
- Capture paths: explicit ("remind me to…") and inferred follow-ups from turns/learning (Phase 07), with confirmation for inferred items.
- Lifecycle: create, update, complete, snooze/defer, cancel, and list/query with natural-language time parsing.
- Recurrence and due handling driven by the scheduler; overdue detection and escalation.
- Proactive reminders delivered on the user's preferred/available channel with acknowledgement handling (done/snooze).
- Tool surface so the agent can manage tasks in-conversation; governed and person-scoped.
- Configuration for default reminder lead times, quiet hours, and delivery channel preference; startup validation.
- Tests for capture, natural-language due parsing, recurrence, cross-channel visibility, proactive firing, and acknowledgement.

## Out of Scope
- Calendar event management (Phase 12) and general if-this-then-that automations.
- A dedicated tasks UI beyond what Phase 09 surfaces (this phase delivers the runtime + tools).

## Entry Criteria
- Scheduler (Phase 07) is available for time-based firing.
- Channels (Phase 06) are available for proactive delivery, including preference/quiet-hours handling.
- Person-scoped identity/memory (Phase 10) so tasks follow the person across channels.

## Exit Criteria
Users can capture tasks/reminders from any channel, have them tracked with due/recurrence handling, and receive proactive reminders on the right channel that they can complete or snooze — all consistent across channels. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
