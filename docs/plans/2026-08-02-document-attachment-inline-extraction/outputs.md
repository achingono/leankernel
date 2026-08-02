# Phase 2026-08-02 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Updated `DocumentLibraryService` | Office document text extraction via `TextExtractionHelper`; try/catch fallback to empty string; `FileSettings` fields injected. | `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/DocumentLibraryService.cs` |
| Updated `AttachmentIngestionMiddleware` | Inline text extraction + request body rewrite for OpenAI content parts (`messages`/`input`); fallback notification for empty/truncated text; `StagedAttachment` record; `InjectExtractedPartsAsync` method. | `src/Services/LeanKernel.Services.Gateway/Providers/AttachmentIngestionMiddleware.cs` |
| Unit tests — DocumentLibraryService | New tests for docx/xlsx extraction routing; existing tests pass. | `test/LeanKernel.Tests.Unit/DocumentIngestion/DocumentLibraryServiceTests.cs` |
| Unit tests — AttachmentIngestionMiddleware | New tests for inline injection (chat + responses), fallback notification, image part preservation; existing tests pass. | `test/LeanKernel.Tests.Unit/Providers/AttachmentIngestionMiddlewareTests.cs` |
| Live probe evidence | Gateway logs showing staged attachment, inline extraction, injected text part in rewritten body, model response referencing document content. | Console/log snippets in `evidence.md` |
| Documentation update | `docs/features/document-ingestion.md` (or new feature doc) describing inline extraction, max-size fallback, and async library pipeline. | Markdown |
| Plan documentation artifacts | Risk register and evidence notes for the phase, including the review note and verification trail. | `risk-register.md`, `evidence.md` |

## Optional Outputs
- Integration test script for docx/xlsx/pptx end-to-end flow (if deemed valuable beyond unit tests).

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] Unit test coverage ≥ 80% on modified classes
- [ ] No Blocker/Critical/Major SonarQube issues
- [ ] Deep review sub-agent passes
- [ ] No credentials in any output
- [ ] Documentation reflects current implementation state