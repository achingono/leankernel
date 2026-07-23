# Phase 05 Exit Criteria

## Gate Checklist
- [x] Filesystem tools operate only within the configured root; traversal/symlink abuse is rejected.
- [x] Database query is read-only by default, parameterized, and safe against injection.
- [x] JSON transform and CSV/XLSX read/write produce correct, tested results.
- [x] `web_fetch`/`http_request` honor egress validation and secret handling.
- [x] Browser tool works when the automation service is up and degrades cleanly when down.
- [x] All new tools are registered through `IToolRegistry` and constrained by governance.
- [x] Unit + integration tests cover boundaries, injection, egress, and degradation.
- [ ] ~~Monitored-folder documents are ingested idempotently into the knowledge store with dedupe.~~ **Moved**: now part of [Phase 21](../phase-21-channel-document-ingestion/index.md) — unified channel-aware document ingestion pipeline with identity scoping and memory policy enforcement. Phase 05's folder-watcher trigger is subsumed as one of multiple ingestion sources in that phase.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
