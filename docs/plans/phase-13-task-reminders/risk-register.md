# Phase 13 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Missed or duplicated reminders | Lost trust | Idempotent scheduler firing + delivery de-dupe | Open |
| R2 | Wrong-time reminders from NL/timezone errors | Poor UX | Explicit tz + relative-time tests; confirm ambiguous times | Open |
| R3 | Reminders during quiet hours | Annoyance | Quiet-hours policy + preferred-channel routing | Open |
| R4 | Silent commitment of inferred tasks | Surprise/clutter | Require confirmation for inferred items | Open |
| R5 | Task list leaks across persons/tenants | Privacy breach | Strict person + tenant scoping; isolation tests | Open |

## Open Decisions
- Natural-language time parser (library vs model-assisted).
- Default quiet hours and whether reminders can override for urgent items.
- Delivery fallback order when the preferred channel is offline.
