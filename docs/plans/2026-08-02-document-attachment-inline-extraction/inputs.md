# Phase 2026-08-02 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Current `DocumentLibraryService` implementation | `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/DocumentLibraryService.cs` (ExtractTextAsync lines 109-140) | Engineering |
| Current `AttachmentIngestionMiddleware` implementation | `src/Services/LeanKernel.Services.Gateway/Providers/AttachmentIngestionMiddleware.cs` (HandleJsonEnvelopeAsync, StageFromJsonEnvelopeAsync, TryReadOpenAiContentPartsAsync) | Engineering |
| `TextExtractionHelper` API | `src/Common/LeanKernel.Logic/Tools/BuiltIn/TextExtractionHelper.cs` (ExtractAsync signature: path, scratchRoot, pythonExecutable, maxExtractedCharacters, ct) | Engineering |
| `FileSettings` configuration | `src/Common/LeanKernel.Logic/Configuration/FileSettings.cs` (RootPath, ScratchRoot, MaxDownloadBytes, MaxExtractedCharacters, PythonExecutable) | Engineering |
| `FileSystemSupport` extension classifiers | `src/Common/LeanKernel.Logic/Tools/BuiltIn/FileSystemSupport.cs` (IsWordOpenXmlCandidate, IsSpreadsheetOpenXmlCandidate, IsPresentationOpenXmlCandidate, IsEpubCandidate, IsLegacyOfficeBinaryCandidate, IsOcrCandidate, RunPythonAsync) | Engineering |
| Existing unit tests | `test/LeanKernel.Tests.Unit/DocumentIngestion/DocumentLibraryServiceTests.cs`, `test/LeanKernel.Tests.Unit/Providers/AttachmentIngestionMiddlewareTests.cs` | Engineering |
| Gateway Docker image runtime | `python3` 3.12.3 at `/usr/bin/python3` (verified in live probe) | Operations |

## Optional Inputs
- Live gateway deployment for end-to-end probe with docx attachment.
- Prior plan template structure (e.g., `docs/plans/2026-07-31-gbrain-import-to-local/`) for formatting consistency.

## Input Validation Checklist
- [ ] `TextExtractionHelper.ExtractAsync` signature confirmed compatible with `FileSettings` fields.
- [ ] Gateway project references `LeanKernel.Logic` (confirmed: ProjectReference in csproj).
- [ ] `MaxExtractedCharacters` default 20,000 is reasonable for inline model context (20K chars ~ 5-7K tokens).
- [ ] PDF and image handling intentionally excluded from inline extraction path.