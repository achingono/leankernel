# Phase 2026-08-02 Document Attachment Inline Extraction

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Enable the model to see attached document content directly when files are sent via OpenAI-compatible content parts (chat completions `messages[].content` and responses `input[].content`). Currently, docx/xlsx/pptx files are ingested into the document library but their `extractedText` is empty because `DocumentLibraryService` only handles text-like extensions and PDF. Additionally, the gateway forwards the original request body with the `file` data URL part unchanged — the downstream model drops non-text parts silently, so it never sees the document content. This phase wires the existing `TextExtractionHelper` (which already handles docx/xlsx/pptx/epub/legacy Office via Python stdlib) into `DocumentLibraryService` for library ingestion, and adds inline text extraction + request-body rewriting in `AttachmentIngestionMiddleware` so the model receives the document text as a `text`/`input_text` part before the request continues downstream.

Review note: this plan has been reviewed and updated to make truncation handling explicit, preserve the shape of the rewritten request body, complete missing documentation artifacts, and incorporate recommendations for optional logger injection in DocumentLibraryService, ScratchRoot fallback guards, and safe handling of duplicate data URLs during request rewriting.

## Scope

## In Scope
- Wire `TextExtractionHelper.ExtractAsync` into `DocumentLibraryService.ExtractTextAsync` for Office document types (epub, docx/docm/dotx/dotm, xlsx/xlsm/xltx/xltm, pptx/pptm/ppsx/ppsm/potx/potm, legacy .doc/.xls/.ppt) with safe fallback (empty string on failure) so ingested documents have `extractedText` populated. Inject optional `ILogger<DocumentLibraryService>? logger = null` to log extraction warnings without breaking existing test constructors, and add `ScratchRoot` fallback resolution when configured empty.
- In `AttachmentIngestionMiddleware.HandleJsonEnvelopeAsync`, after staging OpenAI content-part attachments, extract text inline from the staged files and rewrite the request body to replace each `file`/`image_url`/`file_data` data-URL part with a `text` (chat) or `input_text` (responses) part containing `[Attached file: {name}]\n{extractedText}`. Ensure replacement dictionary mapping uses index assignment to safely handle payloads with duplicate data URLs.
- Fallback behavior: if extraction yields empty text or the result is truncated at `MaxExtractedCharacters`, inject a notification part instead: `Attached document "{name}" was uploaded to the document library. Ingestion is in progress — use the document_search tool to retrieve it after ingestion completes.`
- Preserve existing async ingestion pipeline (event emission, hosted service) — inline extraction is additive for the model context only.
- Unit tests for both changes (DocumentLibraryService office extraction, middleware injection + fallback).

## Out of Scope
- PDF OCR extraction (no OCR libraries installed in gateway image; existing PDF behavior preserved).
- Image multimodal parts (image_url with image/* content-type) — these remain unchanged for vision model support.
- Changing the document tool search/retrieval path — this phase only ensures text reaches the model inline and library.
- Modifications to the GBrain MCP surface or remote GBrain import.

## Entry Criteria
- Repository at commit `851ad66c` (prior fix for GBrain page_not_found dedupe).
- Gateway Docker image has `python3` available at `/usr/bin/python3` (verified).
- `FileSettings` provides `PythonExecutable` ("python3"), `ScratchRoot`, `MaxExtractedCharacters` (20,000), `RootPath`.
- `TextExtractionHelper` exists in `LeanKernel.Logic.Tools.BuiltIn` and handles Office Open XML formats via Python stdlib zipfile + ElementTree.
- Plan reviewed by a separate model/session before implementation.

## Exit Criteria
All checks in [exit-criteria.md](exit-criteria.md) are complete.

## Roles
- Owner: Coding agent
- Reviewer: Separate model/session reviewer
- Approver: Repository maintainer