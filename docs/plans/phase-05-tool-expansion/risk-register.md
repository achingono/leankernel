# Phase 05 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Filesystem tools escape the configured root | Arbitrary file access | Canonical-path checks, symlink rejection, boundary tests | Open |
| R2 | Database query tool enables injection or writes | Data loss/breach | Parameterized queries, read-only default, allowlisted connections | Open |
| R3 | Internet tools reach internal/loopback services | SSRF | Reuse `EgressValidator`; default-deny private ranges | Open |
| R4 | Browser service coupling causes hard failures | Runtime instability | Capability probe + graceful degradation | Open |
| R5 | Ingestion reprocesses/duplicates documents | Noise/cost | Fingerprint dedupe + idempotent queue | Open |
| R6 | New package dependencies bloat or conflict | Build/security risk | Minimal, vetted libraries for XLSX/CSV | Open |

## Open Decisions
- Which XLSX/CSV library to adopt.
- Whether database query ships enabled or disabled-by-default.
- Browser service topology (sidecar container vs shared service).
