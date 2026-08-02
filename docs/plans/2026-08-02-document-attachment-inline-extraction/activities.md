# Phase 2026-08-02 Activities

## Step-By-Step Activities

### Part 1: DocumentLibraryService Office Text Extraction

0. **Plan review.** Review this plan with a separate model/session before implementation.

1. **Update `DocumentLibraryService` constructor** to accept an optional `ILogger<DocumentLibraryService>? logger = null` parameter (preserving compatibility with existing unit test constructors) and store `FileSettings` (not just `RootPath`). Compute a safe scratch root fallback `var scratchRoot = string.IsNullOrWhiteSpace(fileSettings.Value.ScratchRoot) ? Path.Combine(_documentsRoot, "_scratch") : fileSettings.Value.ScratchRoot;` so empty configuration values do not cause `ArgumentException` in path resolution.

2. **Make `ExtractTextAsync` an instance method** (remove `static`) and add `CancellationToken` parameter. Route through `TextExtractionHelper` for Office/EPUB/Legacy types:
   - Use `FileSystemSupport` classifiers to identify:
     - `IsEpubCandidate` → EPUB
     - `IsWordOpenXmlCandidate` → docx/docm/dotx/dotm
     - `IsSpreadsheetOpenXmlCandidate` → xlsx/xlsm/xltx/xltm
     - `IsPresentationOpenXmlCandidate` → pptx/pptm/ppsx/ppsm/potx/potm
     - `IsLegacyOfficeBinaryCandidate` → .doc/.xls/.ppt
   - For these types, call `TextExtractionHelper.ExtractAsync(path, _fileSettings.Value.ScratchRoot, _fileSettings.Value.PythonExecutable, _fileSettings.Value.MaxExtractedCharacters, ct)`.
   - Wrap in try/catch (catch `InvalidOperationException`, `IOException`, `UnauthorizedAccessException`) → on failure log warning and return `string.Empty` (ingestion still succeeds, catalog entry gets empty text).
   - Keep existing behavior for text-like extensions (txt/md/csv/json/xml/html/yaml/yml) and PDF.

3. **Add unit tests** in `DocumentLibraryServiceTests.cs`:
   - `IngestDocumentAsync_DocxFile_ExtractsTextViaTextExtractionHelper` — create a minimal valid docx (or mock the python call? Since unit tests shouldn't spawn python, test the routing logic by verifying `TextExtractionHelper.ExtractAsync` is not called for text files but is called for docx — this may require refactoring to an `ITextExtractor` interface. **Decision**: keep it simple — add integration-style tests that create a real tiny docx file using the stdlib zip approach, or skip python-dependent tests and only test the routing logic for text/PDF. For now, test that `.docx` extension routes to the helper path (can use a fake docx zip file or rely on the existing test infrastructure that uses real temp files). The existing tests use real temp files; we can create a minimal docx zip with `word/document.xml` containing test text.
   - `IngestDocumentAsync_XlsxFile_ExtractsText` — similar with minimal xlsx.
   - Verify existing tests still pass (text, json, binary, PDF behavior unchanged).

### Part 2: AttachmentIngestionMiddleware Inline Text Injection

4. **Define a `StagedAttachment` record** in the middleware to capture staged file metadata for injection:
   ```csharp
   private sealed record StagedAttachment(
       string FileName,
       string StagedPath,
       string DataUrl,
       string ResolvedContentType);
   ```

5. **Modify `StageFromJsonEnvelopeAsync`** to return `IReadOnlyList<StagedAttachment>` instead of `void`. For each successfully staged non-image attachment, add to the returned list.

6. **Add inline extraction helper** `ExtractInlineTextAsync(StagedAttachment, FileSettings, logger, ct)`:
   - Skip if `ResolvedContentType.StartsWith("image/")` or is PDF (no OCR in gateway).
   - Call `TextExtractionHelper.ExtractAsync(stagedPath, settings.ScratchRoot, settings.PythonExecutable, settings.MaxExtractedCharacters, ct)`.
   - Catch exceptions → log warning, return empty string.
   - Treat the result as truncated only when the helper output contains the standard truncation suffix `"[Content truncated to "` or when the extracted text length exceeds `settings.MaxExtractedCharacters` before the call returns. This avoids misclassifying a non-truncated result that is exactly at the limit.

7. **Add injection rewrite method** `InjectExtractedPartsAsync(HttpContext, IReadOnlyList<StagedAttachment>, FileSettings, logger, ct)`:
   - Build a `Dictionary<string dataUrl, string replacementText>` using index assignment (`dict[attachment.DataUrl] = replacement`) to safely handle potential duplicate data URLs in the request payload without throwing key collision exceptions:
     - For each staged attachment:
       - `text = await ExtractInlineTextAsync(...)`
       - If `string.IsNullOrWhiteSpace(text)` OR `IsTruncated(text)`: 
         - `replacement = $"[Attached document \"{fileName}\" was uploaded to the document library. Ingestion is in progress — use the document_search tool to retrieve it after ingestion completes.]"`
       - Else:
         - `replacement = $"[Attached file: {fileName}]\n{text}"`
   - Parse the request body as `JsonNode` (mutable), then rewrite only the specific attachment parts that match the staged data URLs while preserving the rest of the JSON structure.
   - Preserve the existing request shape: for chat completions, replace matching `content` array items with a `text` part; for responses, replace matching items with an `input_text` part. Leave non-matching parts, sibling properties, and message ordering intact.
   - If the original `content` is a string data URL (rare), replace it with an array containing a single replacement part rather than losing the surrounding message structure.
   - Serialize the modified `JsonNode` to bytes, replace `context.Request.Body = new MemoryStream(bytes)`, `context.Request.ContentLength = bytes.Length`, and reset the stream to the start before `next(context)`.

8. **Wire injection in `HandleJsonEnvelopeAsync`**:
   - Track whether attachments came from OpenAI content parts (`bool isOpenAiParts`).
   - Call `var staged = await StageFromJsonEnvelopeAsync(...)` which now returns the list.
   - If `isOpenAiParts && staged.Count > 0`:
     - `await InjectExtractedPartsAsync(context, staged, fileSettings.Value, logger, context.RequestAborted);`
   - Then `await next(context);`

9. **Add unit tests** in `AttachmentIngestionMiddlewareTests.cs`:
   - `InvokeAsync_ChatCompletionsWithDocxFilePart_InjectsExtractedTextAndInvokesNext` — send a chat completions request with a `file` part containing a tiny docx data URL (base64 of a minimal docx zip). Verify: event emitted, `next` invoked, AND the rewritten body contains a `text` part with the extracted content (not the original data URL). Since python extraction runs, the test will actually spawn python — acceptable for unit test (uses stdlib). Alternatively, create a `.md` file attachment (text-like) to test injection without python; add a second test with `.docx` that verifies the fallback path if python fails.
   - `InvokeAsync_ChatCompletionsWithLargeDoc_InjectsFallbackNotification` — test the truncation/fallback path by setting `MaxExtractedCharacters` very small (e.g., 50) and sending a text file > 50 chars.
   - `InvokeAsync_ResponsesApiWithInputFile_InjectsInputTextPart` — verify "input_text" type used for `input` container.
   - `InvokeAsync_ImageUrlPart_NotReplaced` — image_url with image/png content-type should remain unchanged.
   - Existing tests must still pass.

### Part 3: Verification & Quality Gates

10. **Run full unit test suite** (`dotnet test`). Ensure ≥80% coverage on modified files.

11. **Rebuild and redeploy gateway**:
    ```bash
    docker compose up -d --build gateway
    ```

12. **Live probe** — send a chat completions request with a docx attachment (base64 data URL) to `/v1/chat/completions`. Verify:
    - Response contains document content (model answers based on inline text).
    - Gateway logs show "Staged JSON attachment for ingestion" + "Injected extracted text for {fileName}".
    - Document library has entry with non-empty `extractedText`.

13. **Run quality gates**:
    - `scripts/quality/sonarqube-scan.sh` — address all Blocker/Critical/Major issues.
    - Deep review sub-agent.

14. **Update documentation** — update `docs/features/document-ingestion.md` (or equivalent) to describe inline extraction behavior and fallback.

15. **Commit changes** — only files belonging to this session (leave other session's uncommitted files alone).