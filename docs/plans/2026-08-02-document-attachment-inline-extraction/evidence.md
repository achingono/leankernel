# Phase 2026-08-02 Evidence

## Review Evidence
- Plan reviewed and updated on 2026-08-02 to tighten truncation detection, make request-body rewrite behavior explicit, fill in missing documentation artifacts, and incorporate recommendations (optional logger injection, scratch path guards, key-collision-safe dictionary mapping).
- Separate-model plan review completed before implementation.

## Implementation Evidence
- `DocumentLibraryService.ExtractTextAsync` now routes Office Open XML/EPUB/legacy binary formats through `TextExtractionHelper` with a try/catch fallback (empty `extractedText`), optional `ILogger`, and empty-`ScratchRoot` fallback to `{documentsRoot}/_scratch`. Text-like and PDF behavior unchanged.
- `AttachmentIngestionMiddleware` — `StageFromJsonEnvelopeAsync` returns `StagedAttachment` items; `InjectExtractedPartsAsync` rewrites the forwarded body, replacing each staged non-image OpenAI part with a `{type:"text"|"input_text"}` part (`[Attached file: {name}]\n{text}`) or the `document_search` fallback notification for empty/truncated extraction. Image `image/*` parts pass through; content-length and body position are restored/replaced correctly; no rewrite leaves the original body intact.
- Unit tests added and passing (961 total, 0 failures):
  - DocumentLibraryService: docx via `TextExtractionHelper`, xlsx, deterministic Python-unavailable fallback (empty text), existing text/PDF/binary behavior.
  - AttachmentIngestionMiddleware: chat-completions markdown injection, docx injection via Python, truncated→fallback notification, responses `input_text` injection, image-part preservation, and all pre-existing staging/event tests.
- Read coverage vs threshold: `DocumentLibraryService` line 100% / branch 85.7%; `AttachmentIngestionMiddleware` line 88.5% / branch 72.2% (gate ≥ 80%).
- Unit suite: `dotnet test` → 961 passed, 0 failed, duration ~19 s.
- SonarQube scan: `scripts/quality/sonarqube-scan.sh` → **QUALITY GATE: PASSED** (no Blocker/Critical/Major introduced).
- Deep review sub-agent run — findings and disposition:
  - Addressed inline: orphaned Python processes are now killed (process tree) when the cancellation token fires (`FileSystemSupport.RunPythonAsync`); `SanitizeFileName` strips control characters so untrusted file names cannot inject into logs or the prompt annotation.
  - Deferred (recorded, pre-existing/out-of-phase contracts): single-pass body parsing, multipart size cap parity, archive decompression bomb guard inside the Python extractors, PDF catalog text gap (OCR intentionally out of scope), and image-parts declared without an `image/*` MIME being staged/replaced. These are documented for a follow-up phase.
- Live probe (gateway deploy + docx attachment via `/v1/chat/completions`) is outstanding in a deployment environment.
