# Phase 07 Exit Criteria

## Gate Checklist
- [ ] Completed turns enqueue asynchronously without blocking or slowing the response.
- [ ] The learning worker runs fact/intent/gap/engagement steps in order, idempotently.
- [ ] Learned knowledge is written back under correct tenant/user/channel scope (scope-relative keys).
- [ ] Onboarding gap detection produces directives from learned identity intent when data is missing.
- [ ] Cron-defined jobs are evaluated and executed on schedule via the scheduler hosted service.
- [ ] Scheduled-job entities/repository persist with a valid EF migration.
- [ ] Native GBrain Dream runs are schedulable with source scoping and bounded execution windows.
- [ ] Dream runs persist phase-level outcomes for diagnostics and replay workflows.
- [ ] Worker/scheduler failures are isolated and logged with actionable context (no broad swallowing).
- [ ] Unit + integration tests cover queue, steps, write-back, cron evaluation, and job execution.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
