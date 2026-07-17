# Phase 05 Exit Criteria

## Gate Checklist
- [x] Filesystem tools operate only within the configured root; traversal/symlink abuse is rejected.
- [x] Database query is read-only by default, parameterized, and safe against injection.
- [x] JSON transform and CSV/XLSX read/write produce correct, tested results.
- [x] `web_fetch`/`http_request` honor egress validation and secret handling.
- [x] Browser tool works when the automation service is up and degrades cleanly when down.
- [ ] Monitored-folder documents are ingested idempotently into the knowledge store with dedupe.
- [x] All new tools are registered through `IToolRegistry` and constrained by governance.
- [x] Unit + integration tests cover boundaries, injection, egress, and degradation (ingestion tests pending with document ingestion implementation).

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
