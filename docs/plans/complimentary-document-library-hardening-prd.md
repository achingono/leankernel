# Complimentary Document Library Hardening PRD

## Objective

Harden the complimentary document library implementation for safety, data integrity, and operational reliability without changing its core UX (upload, browse, inspect, download).

## Context

A focused implementation review identified four high-impact risks:

1. Frontmatter corruption risk from unsanitized title/tag values.
2. Silent overwrite risk for duplicate filenames/titles.
3. Non-atomic ingest flow and success responses when binary upload/linking fails.
4. Document browse masking outages as empty state and truncating results at a hard-coded limit.

This plan was cross-reviewed by a different model and updated with required adjustments before implementation.

## Reviewed Decisions

1. **Frontmatter safety**
   - Replace ad-hoc YAML string concatenation with a dedicated serializer/parser helper.
   - Normalize and quote scalar values safely.
   - Persist both `source_file` and `storage_path` metadata fields.

2. **Collision-safe ingest**
   - Store uploaded binaries under unique filenames.
   - Ensure page slug uniqueness under `doc/` by probing existing pages and suffixing (`-2`, `-3`, ...).

3. **Fail-fast ingest semantics**
   - Treat binary upload/linking as required for ingest success.
   - Throw on `file_upload` failure or missing storage path.
   - Clean up saved local file on downstream failure paths.

4. **Browse/UI robustness**
   - Add pagination parameters to document browse service.
   - Stop converting all failures into empty result sets.
   - Parse frontmatter once and resolve download path using `storage_path` first, then `source_file` fallback.

5. **Test coverage**
   - Add unit tests for slug collisions, required upload path handling, and browse/error behavior.

## Implementation Plan

### Phase 1 — Shared metadata helper

- Add a small helper in Gateway services to parse frontmatter for document pages.
- Add a serializer helper in Tools for writing safe frontmatter values.

### Phase 2 — Ingest hardening

- Update `DocumentLibraryService` to:
  - sanitize metadata values,
  - generate unique storage filename,
  - generate unique `doc/` slug,
  - require successful `file_upload` path extraction,
  - include `storage_path` in frontmatter,
  - clean up local file on failures.

### Phase 3 — Browse/download flow hardening

- Update `DocumentUiService` to:
  - accept page/limit arguments,
  - pass pagination params to `list_pages`,
  - propagate failures to caller (no silent empty fallback).
- Update `Knowledge.razor` to:
  - request paged document browse,
  - resolve download path via parsed frontmatter (`storage_path` then `source_file`).

### Phase 4 — Tests and quality gates

- Add/extend unit tests for:
  - document ingest collision and upload-required behavior,
  - document browse parsing and error propagation.
- Run full repository validation and quality scans.

## Files In Scope

- `src/LeanKernel.Tools/DocumentLibraryService.cs`
- `src/LeanKernel.Gateway/Services/DocumentUiService.cs`
- `src/LeanKernel.Gateway/Components/Pages/Knowledge.razor`
- new/updated unit test files in `test/LeanKernel.Tests.Unit/...`
- this PRD in `docs/plans/`

## Validation Sequence

From repository root:

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal
scripts/quality/test-coverage.sh
scripts/quality/sonarqube-scan.sh
docker compose build
```
