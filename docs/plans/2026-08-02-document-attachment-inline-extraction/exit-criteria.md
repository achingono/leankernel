# Phase 2026-08-02 Exit Criteria

## Gate Checklist
- [x] Plan reviewed by a separate model/session before implementation.
- [x] `DocumentLibraryService.ExtractTextAsync` routes Office/EPUB/Legacy types through `TextExtractionHelper.ExtractAsync` with `FileSettings` parameters (PythonExecutable, ScratchRoot, MaxExtractedCharacters), accepting optional `ILogger<DocumentLibraryService>? logger = null` and resolving empty `ScratchRoot` with a fallback.
- [x] Extraction failures (python missing, invalid zip, etc.) are caught, logged, and return empty string — ingestion job still succeeds (catalog entry created, `extractedText` empty).
- [x] Existing text-like (txt/md/csv/json/xml/html/yaml/yml) and PDF extraction behavior unchanged.
- [x] `AttachmentIngestionMiddleware.HandleJsonEnvelopeAsync` returns `StagedAttachment` list from `StageFromJsonEnvelopeAsync`.
- [x] For OpenAI content-part requests (`messages` or `input` arrays), middleware rewrites the request body:
  - Each staged non-image `file`/`image_url`/`file_data` data-URL part is replaced with a `text` (chat) or `input_text` (responses) part.
  - Replacement text = `[Attached file: {name}]\n{extractedText}` when extraction succeeds and is not truncated.
  - Replacement text = fallback notification instructing model to use `document_search` after ingestion when extraction is empty or truncated (≥ MaxExtractedCharacters).
  - Image parts (`image/*` content-type) are NOT replaced — they pass through for vision models.
- [x] Async ingestion pipeline unchanged: `DocumentIngestionRequestedEvent` still emitted for each staged attachment.
- [x] Unit tests added and passing:
  - `DocumentLibraryServiceTests`: docx, xlsx extraction tests + all existing tests pass.
  - `AttachmentIngestionMiddlewareTests`: inline injection (chat + responses), fallback, image preservation + all existing tests pass.
- [x] Full unit test suite passes (`dotnet test`): 961 tests, coverage ≥ 80% on modified files (`DocumentLibraryService` 100%, `AttachmentIngestionMiddleware` 88.5%).
- [ ] Gateway rebuilt and redeployed via `docker compose up -d --build gateway`; health checks pass.
- [ ] Live probe: POST `/v1/chat/completions` with a docx attachment (base64 data URL in `messages[].content[].file.file_data`) → response includes document content (proves inline text reached the model).
- [ ] Gateway logs confirm: "Staged JSON attachment for ingestion", "Injected extracted text for {fileName}", event emitted.
- [ ] Document library entry for the probed file has non-empty `extractedText`.
- [x] `scripts/quality/sonarqube-scan.sh` — no Blocker, Critical, or Major issues (QUALITY GATE: PASSED).
- [x] Deep review sub-agent — findings addressed (Python process-tree kill on cancellation; filename control-char sanitization); remaining items are pre-existing/out-of-phase and recorded in evidence.
- [x] Documentation updated to reflect inline extraction and fallback behavior.
- [ ] Only session-specific files committed (other session's uncommitted files untouched) — pending commit.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |