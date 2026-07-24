# Phase 07 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Turn-event queue | Bounded async queue fed by turn completion | C# source |
| Learning worker + pipeline | Background worker + ordered self-improvement steps | C# source |
| Learning steps | Fact/intent/gap/engagement steps | C# source |
| Knowledge update coordinator | Scope-correct write-back to knowledge/memory | C# source |
| Onboarding intelligence | Gap detector + directive builder | C# source |
| Scheduler | Cron evaluator, job executor, hosted service, time boundary | C# source |
| Dream orchestration job | Scheduler-owned native Dream execution with source and window controls | C# source |
| Job persistence | Scheduled-job entities/repository + migration | C# + EF migration |
| Configuration + validation | Learning + scheduler settings | C# + appsettings |
| Tests | Queue, steps, write-back, cron, jobs coverage | xUnit projects |
| Documentation | Learning + onboarding + scheduler feature docs | Markdown |

## Optional Outputs
- Learning/scheduler signals surfaced to Phase 08 diagnostics.
- Dream phase reports exported for replay/eval consumers (Phase 23).

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
