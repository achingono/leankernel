# Phase 13 Activities

## Step-By-Step Activities
1. Define the person-scoped task/reminder entity (title, notes, due, recurrence, status, source, priority) and add EF entities/migrations.
2. Implement explicit capture in-conversation ("remind me to…") with natural-language due/recurrence parsing and timezone awareness.
3. Implement inferred capture from turns/learning (Phase 07), requiring user confirmation before committing inferred items.
4. Implement lifecycle operations: create, update, complete, snooze/defer, cancel, and list/query.
5. Wire recurrence and due firing to the scheduler (Phase 07); implement overdue detection and escalation.
6. Implement proactive reminder delivery via channels (Phase 06) using the person's preferred/available channel and quiet-hours, with acknowledgement (done/snooze) handling.
7. Expose a governed, person-scoped task-management tool so the agent can operate tasks in any conversation.
8. Add configuration (default lead times, quiet hours, delivery preference) and startup validation.
9. Add tests: capture, NL due parsing, recurrence, cross-channel visibility, proactive firing, and acknowledgement handling.
10. Document task/reminder management in `docs/features/`.

## Review Focus
- Tasks are person-scoped and visible/actionable across channels.
- NL time parsing is correct across timezones and relative expressions.
- Proactive delivery respects quiet hours and preferred channel.
- Inferred tasks require confirmation (no silent commitments).
- Recurrence firing is idempotent (no duplicate reminders).
