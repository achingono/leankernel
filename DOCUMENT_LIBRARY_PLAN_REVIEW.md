# Document Library Feature Plan Review

**Date:** 2025-05-25  
**Reviewer:** Code Quality Agent  
**Plan Status:** CONDITIONAL APPROVAL with Required Adjustments

---

## Executive Summary

The plan **correctly identifies all four critical issues** and proposes sound mitigations that align with LeanKernel's architecture patterns. However, **5 specific gaps and 2 risky assumptions** require adjustment before implementation.

**Verdict:** ✅ **Approve with revisions.** Proceed only after addressing Required Adjustments (Section 2).

---

## 1. Issue Validation & Code Evidence

### Issue #1: YAML/Frontmatter Injection Risk ✅ CONFIRMED

**Evidence:**
- **File:** `DocumentLibraryService.cs:115–121`
- **Root cause:** Raw string concatenation with `.Replace("\"", "\\\"")` is insufficient for YAML safety.
  ```csharp
  var frontmatterYaml = "---" + Environment.NewLine +
                        $"title: \"{finalTitle.Replace("\"", "\\\"")}\"" + ... // Vulnerable
                        $"tags: [" + string.Join(", ", cleanTags.Select(t => ...)) + "]" + ...
  ```
- **Injection vectors:** 
  - CR/LF in title → breaks YAML block structure
  - Quotes and colons in tags → type coercion / syntax error in YAML parsers
  - No quoting of numeric-looking strings → YAML parses as numbers instead of strings

**Mitigation A is sound.** But see "Required Adjustment #1" below.

---

### Issue #2: Duplicate Upload Collision ✅ CONFIRMED

**Evidence:**
- **File:** `DocumentLibraryService.cs:71–73`
  ```csharp
  var cleanFilename = Path.GetFileName(filename);
  var relativePath = Path.Combine("documents", cleanFilename);
  var targetFullPath = Path.Combine(documentsDir, cleanFilename);
  // FileMode.Create overwrites existing file with same name
  ```
- **Wiki slug:** `lines 105–111` has **zero collision detection**
  ```csharp
  var pageSlug = $"doc/{baseSlug}";
  await _knowledgeService.PutPageAsync(pageSlug, markdownContent, ct); // No GetPageAsync check
  ```
- **Consequence:** Two users uploading "proposal.pdf" with title "Q1 Report" → second file and wiki page overwrites first.

**Mitigation B is sound.** See clarification needed in "Required Adjustment #2".

---

### Issue #3: Non-Atomic Ingestion ✅ CONFIRMED

**Evidence:**
- **File:** `DocumentLibraryService.cs:133–152`
  ```csharp
  try {
      var uploadResult = await _gBrainClient.CallToolAsync("file_upload", ...);
      if (uploadResult is { } res && res.TryGetProperty("path", out var pathProp)) {
          fileStoragePath = pathProp.GetString();
      }
  }
  catch (Exception ex) {
      _logger.LogWarning(ex, "..."); // Silent degradation
  }
  ```
  **file_upload is optional.** Service continues on failure.

- **Line 160:** Fallback returns wrong path if upload failed:
  ```csharp
  FileStoragePath = fileStoragePath ?? relativePath // Misleading
  ```

- **Cleanup:** Only happens on extraction failure (lines 90–100), **not** on `PutPageAsync` failure.
  ```csharp
  await _knowledgeService.PutPageAsync(pageSlug, markdownContent, ct); // No try/catch, no cleanup
  ```

**Mitigation C is sound.** See critical clarification in "Required Adjustment #3".

---

### Issue #4: UI Error Masking & Hard Limits ✅ CONFIRMED

**Evidence:**
- **File:** `DocumentUiService.cs:117–121`
  ```csharp
  catch (Exception ex) {
      _logger.LogWarning(ex, "Failed to browse documents from GBrain. Continuing with empty list.");
      return []; // All errors masked
  }
  ```
  Masks transport, JSON, timeout, and GBrain-specific errors equally.

- **Hard limit:** `line 59` → `limit = 100` (no pagination).

- **Storage path extraction:** `Knowledge.razor:1133`
  ```csharp
  var match = Regex.Match(page.Content, @"source_file:\s*""?([^""\r\n]+)""?");
  // Brittle: depends on exact YAML formatting, doesn't handle escaped quotes
  ```

**Mitigations D & E are sound.** See "Required Adjustment #4" for specificity.

---

## 2. Required Adjustments

### Adjustment #1: Specify YAML Serialization Library & Frontmatter Schema

**Current Plan says:** "replace ad-hoc frontmatter builder with a safe serializer helper"

**Issue:** The plan doesn't specify:
1. **Which YAML library?** (YamlDotNet? System.Yaml?)
2. **Frontmatter structure for storage_path?**

**Required Decision:**
- **Recommendation:** Use `YamlDotNet` (NuGet `YamlDotNet`); it's industry-standard in .NET.
- **Frontmatter design change needed:** Current schema has only `source_file`. Plan must clarify new schema:
  ```yaml
  ---
  type: document
  title: "User Title Here"
  source_file: "documents/filename.pdf"
  storage_path: "gbrain-internal-storage-id-or-path" # NEW
  imported_at: "2025-05-25T..."
  tags: ["tag1", "tag2"]
  ---
  ```
- **Serialization helper signature:**
  ```csharp
  public static string BuildFrontmatter(string title, string sourceFile, string? storagePath, 
                                         DateTimeOffset importedAt, List<string> tags)
  {
      var frontmatter = new Dictionary<string, object>
      {
          ["type"] = "document",
          ["title"] = title,
          ["source_file"] = sourceFile,
          ["imported_at"] = importedAt,
          ["tags"] = tags
      };
      if (!string.IsNullOrEmpty(storagePath))
          frontmatter["storage_path"] = storagePath;
      
      var yaml = new YamlBuilder().BuildFrontmatter(frontmatter);
      return $"---{Environment.NewLine}{yaml}---";
  }
  ```
  This ensures YAML syntax correctness, proper quoting, and safe CR/LF stripping.

**Action:** Add this helper to `LeanKernel.Tools` (new file: `FrontmatterHelper.cs`). Reuse in:
- `DocumentLibraryService` (frontmatter building)
- `DocumentUiService.BrowseDocumentsAsync` (frontmatter parsing for UI display)
- `Knowledge.razor` (frontmatter parsing for download URL extraction)

---

### Adjustment #2: Clarify Slug Uniqueness Scope & Collision Strategy

**Current Plan says:** "Generate a unique wiki slug under doc/ by probing IKnowledgeService.GetPageAsync and appending -2, -3 as needed."

**Issue:** Ambiguity about scope and failure handling.

**Required Clarifications:**

1. **Scope:** Are slugs **globally unique** across all users, or **user-scoped** (e.g., `doc/{userid}/{slug}`)?
   - Current code suggests **global** (`doc/{baseSlug}`).
   - Recommendation: Keep global for simplicity, but document it.

2. **Collision probe logic:**
   ```csharp
   string pageSlug = $"doc/{baseSlug}";
   int attempt = 0;
   while (await _knowledgeService.GetPageAsync(pageSlug, ct) != null) {
       attempt++;
       if (attempt > 10) throw new InvalidOperationException("Too many slug collisions");
       pageSlug = $"doc/{baseSlug}-{attempt + 1}"; // -2, -3, etc.
   }
   ```

3. **Resilience assumption:** `GetPageAsync` is wrapped by `ResilientKnowledgeService`, which:
   - Caches results (can return stale data if GBrain is unhealthy).
   - **Swallows exceptions** and returns cached value or null.
   - This could mask transient failures in collision detection!

   **Recommendation:** Document assumption that caching is acceptable for collision detection (i.e., stale read is low-risk because slugs are immutable once created). Alternatively, add a "force-fresh" mode to ResilientKnowledgeService for critical paths.

4. **What if GetPageAsync itself throws** (not caught by Resilience)?
   - Plan should add explicit `try/catch` around probe loop:
     ```csharp
     try {
         // Probe loop here
     } catch (GBrainException ex) when (ex.IsTransient) {
         // Log and retry probe with backoff
     } catch {
         // Unrecoverable; throw
     }
     ```

**Action:** Define these details in the final implementation spec.

---

### Adjustment #3: Clarify File Upload Failure Handling & Atomicity Guarantees

**Current Plan says:** "Make file_upload required. If upload fails or response lacks storage path, throw."

**Issue:** This changes ingestion atomicity semantics. Need explicit decision on failure boundaries.

**Current behavior:**
- Save file (can fail) → Extract text (can fail + cleanup) → Save wiki page (can fail) → Upload file (optional).

**Proposed behavior:**
- Save file → Extract → Save wiki page → **Upload file (required)** → Return success

**Critical ambiguity:** What happens if `PutPageAsync` succeeds but file cleanup fails afterward?

**Recommended implementation:**
```csharp
// Step 1: Save local file
await using (var writeStream = new FileStream(targetFullPath, FileMode.Create, ...)) {
    await fileStream.CopyToAsync(writeStream, ct);
}

try {
    // Step 2: Extract
    extractedText = await TextExtractionHelper.ExtractAsync(targetFullPath, ...);
    
    // Step 3: Prepare wiki page with placeholder storage_path
    var frontmatterYaml = BuildFrontmatter(title, relativePath, storagePath: null, ...); // Unknown yet
    var markdownContent = frontmatterYaml + Environment.NewLine + extractedText;
    
    // Step 4: Save wiki page
    await _knowledgeService.PutPageAsync(pageSlug, markdownContent, ct);
    
    // Step 5: NOW upload file (required)
    var uploadResult = await _gBrainClient.CallToolAsync("file_upload", new {
        path = targetFullPath,
        page_slug = pageSlug
    }, ct); // Will throw on failure
    
    // Extract actual storage_path from response
    if (uploadResult?.ValueKind == JsonValueKind.Object &&
        uploadResult.Value.TryGetProperty("path", out var pathProp)) {
        fileStoragePath = pathProp.GetString();
    } else {
        throw new InvalidOperationException("file_upload response missing 'path' field");
    }
    
    // Step 6: UPDATE wiki page with real storage_path
    var finalFrontmatter = BuildFrontmatter(title, relativePath, fileStoragePath, ...);
    var finalContent = finalFrontmatter + Environment.NewLine + extractedText;
    await _knowledgeService.PutPageAsync(pageSlug, finalContent, ct);
    
    return new DocumentIngestionResult { ... };
} catch {
    // Cleanup: Delete local file AND wiki page
    try { File.Delete(targetFullPath); } catch { }
    try { 
        await _knowledgeService.DeletePageAsync(pageSlug, ct); 
        // NOTE: DeletePageAsync doesn't exist! May need to add or skip this.
    } catch { }
    throw;
}
```

**Three explicit decisions needed:**
1. **Two-pass wiki write?** Should frontmatter be updated post-upload to include correct `storage_path`?
   - Pros: Wiki contains accurate reference; no fallback needed.
   - Cons: Two writes; second write can fail.
   - **Recommendation:** YES. This is more robust than fallback.

2. **Wiki page cleanup on failure?** Should failed pages be deleted or marked as orphaned?
   - Current `IKnowledgeService` has no `DeletePageAsync`.
   - **Recommendation:** Add `DeletePageAsync` or at least acknowledge orphan pages may exist and document cleanup procedure.

3. **File cleanup after second write?** If second `PutPageAsync` fails, should we delete the local file?
   - **Recommendation:** YES. File should not persist if wiki page cannot be updated.

**Action:** Revise C) to include two-pass wiki write and explicit error boundaries.

---

### Adjustment #4: Specify Error Categories for UI Error Propagation

**Current Plan says:** "only catch expected transport/JSON/GBrain errors"

**Issue:** Vague. Need explicit exception list.

**Recommendation:** Define three categories:

1. **Transient errors (log & retry or degrade gracefully):**
   - `HttpRequestException` (transport)
   - `OperationCanceledException` (timeout)
   - `TimeoutException`
   - `GBrainException` with `is_transient` flag (if added to GBrainException)

   ```csharp
   catch (HttpRequestException ex) when (ex.InnerException is TimeoutException) {
       _logger.LogWarning(ex, "List_pages timed out");
       return []; // Acceptable fallback
   }
   ```

2. **Application errors (propagate with detail):**
   - `JsonException` (malformed response)
   - `GBrainException` with specific error code (parse and re-throw as domain error)

   ```csharp
   catch (JsonException ex) {
       throw new InvalidOperationException("GBrain returned invalid JSON for list_pages", ex);
   }
   ```

3. **Unexpected errors (fail loudly):**
   - `NotImplementedException`, `NullReferenceException`, etc.
   - These should **not** be caught.

**For DocumentUiService.BrowseDocumentsAsync:**
```csharp
public async Task<List<KnowledgePageSummary>> BrowseDocumentsAsync(int page = 1, int limit = 50, CancellationToken ct = default) {
    try {
        var result = await _gBrainClient.CallToolAsync(
            "list_pages",
            new { type = "document", skip = (page - 1) * limit, limit },
            ct);
        
        // Parse with error detail
        return ParsePages(result) ?? [];
    }
    catch (HttpRequestException ex) when (ex.InnerException is TimeoutException) {
        _logger.LogWarning(ex, "GBrain list_pages timed out");
        return []; // Graceful fallback
    }
    catch (JsonException ex) {
        throw new InvalidOperationException("GBrain returned malformed JSON", ex);
    }
    catch (GBrainException ex) {
        throw; // Let caller handle
    }
    // Do NOT catch generic Exception
}
```

**Action:** Define and document `GBrainException` subtypes or flags. Update DocumentUiService with categorical error handling.

---

### Adjustment #5: Add Pagination Implementation Details

**Current Plan says:** "DocumentUiService.BrowseDocumentsAsync: add page/limit args"

**Issue:** Current `list_pages` tool hardcodes `limit = 100`. Unclear if MCP supports pagination.

**Recommended signature:**
```csharp
public async Task<(List<KnowledgePageSummary> Items, int TotalCount)> BrowseDocumentsAsync(
    int page = 1, 
    int pageSize = 50, 
    CancellationToken ct = default)
{
    const int maxPageSize = 100;
    var size = Math.Min(pageSize, maxPageSize);
    var skip = (page - 1) * size;
    
    var result = await _gBrainClient.CallToolAsync("list_pages", new {
        type = "document",
        skip, // or offset?
        limit = size
    }, ct);
    // ...
}
```

**But:** Verify MCP `list_pages` actually supports `skip/offset`. If not, client-side pagination is necessary but wasteful.

**Action:** Document whether GBrain MCP `list_pages` supports cursor/offset/skip parameters. If not, add pagination as a follow-up task.

---

## 3. Gaps: Test Coverage Requirements

The plan's Section E (Tests) is sound but incomplete. Add these specific scenarios:

### Unit Tests: DocumentLibraryService

1. **Collision scenarios:**
   - `[Fact] IngestDocumentAsync_appends_numeric_suffix_on_slug_collision()` — Verify retry logic works with GetPageAsync returning existing page.
   - `[Fact] IngestDocumentAsync_throws_after_max_collision_attempts()` — Verify bounded retry.
   - `[Fact] IngestDocumentAsync_uses_deterministic_slug_from_title()` — Verify normalization.

2. **File upload required:**
   - `[Fact] IngestDocumentAsync_throws_if_file_upload_missing_path_field()` — Verify response validation.
   - `[Fact] IngestDocumentAsync_throws_if_file_upload_fails()` — Verify propagation.
   - `[Fact] IngestDocumentAsync_cleans_up_files_on_upload_failure()` — Verify file and wiki page deleted.

3. **YAML safety:**
   - `[Theory] BuildFrontmatter_escapes_special_characters(string input)` — CR/LF, quotes, colons, etc.
   - `[Fact] BuildFrontmatter_includes_storage_path_when_provided()` — Verify new field.
   - `[Fact] BuildFrontmatter_omits_storage_path_when_null()` — First pass vs second pass.

4. **Atomicity:**
   - `[Fact] IngestDocumentAsync_deletes_local_file_on_put_page_failure()` — Verify cleanup.
   - `[Fact] IngestDocumentAsync_updates_wiki_page_with_real_storage_path()` — Two-pass write.

### Unit Tests: DocumentUiService

1. **Error propagation:**
   - `[Fact] BrowseDocumentsAsync_propagates_JsonException()` — Verify not masked.
   - `[Fact] BrowseDocumentsAsync_returns_empty_on_timeout()` — Verify graceful fallback.
   - `[Fact] BrowseDocumentsAsync_propagates_GBrainException()` — Verify not masked.

2. **Pagination:**
   - `[Fact] BrowseDocumentsAsync_uses_provided_page_and_limit()` — Verify parameters passed to MCP.
   - `[Fact] BrowseDocumentsAsync_defaults_to_page_1_limit_50()` — Verify sensible defaults.

3. **Parsing:**
   - `[Theory] GetDownloadUrlAsync_extracts_storage_path_from_frontmatter(string yaml)` — Regex brittleness.
   - `[Fact] GetDownloadUrlAsync_falls_back_to_source_file_if_storage_path_absent()` — Backward compat.

### Integration Tests

1. **End-to-end ingestion:**
   - Mock GBrainMcpClient with file_upload response.
   - Verify two wiki writes occur (once without path, once with).
   - Verify local file exists and can be deleted.

2. **Collision handling:**
   - Mock GetPageAsync to return existing pages for baseSlug, baseSlug-1, baseSlug-2.
   - Verify probe stops at first available slug.

---

## 4. Risky Assumptions & Verification Checklist

| Assumption | Risk | Mitigation |
|-----------|------|-----------|
| `GetPageAsync` returns null for missing pages (not throws) | Medium | ✅ Verified in `GBrainKnowledgeService:82–85` |
| `ResilientKnowledgeService` caching is acceptable for collision detection | Medium | ⚠️ Document explicitly; consider force-fresh mode |
| MCP `list_pages` can accept `skip` or `offset` for pagination | High | ❌ Verify with GBrain maintainers before implementation |
| GBrain `file_upload` always returns JSON with `path` field | Medium | ⚠️ Add explicit validation; throw if missing |
| Frontmatter YAML can be round-tripped without data loss | Low | ✅ Use YamlDotNet for serialization + deserialization tests |
| xUnit + Moq + FluentAssertions is appropriate | Low | ✅ Verified in existing tests |
| Two-pass wiki write doesn't create race condition | Medium | ⚠️ Document assumption that slug is immutable between writes |

---

## 5. Implementation Sequence Recommendation

**Phase 1 (Low-risk infrastructure):**
1. Add `YamlDotNet` NuGet dependency.
2. Create `FrontmatterHelper.cs` with `BuildFrontmatter()` and `ParseFrontmatter()`.
3. Add unit tests for FrontmatterHelper (YAML safety, edge cases).
4. Update `DocumentLibraryService` to use FrontmatterHelper (no logic change yet).

**Phase 2 (Collision & atomicity):**
1. Add collision probe loop to `IngestDocumentAsync`.
2. Implement two-pass wiki write (with first write having null storage_path).
3. Make file_upload required; add explicit validation of response.
4. Add cleanup on all failure paths.
5. Unit test all three changes.

**Phase 3 (UI hardening):**
1. Add pagination to `BrowseDocumentsAsync` (signature change; update controller).
2. Implement categorical error handling (transient vs application).
3. Use `FrontmatterHelper.ParseFrontmatter()` instead of regex.
4. Update `Knowledge.razor` to use frontmatter parser.
5. Unit + integration tests.

**Phase 4 (Polish & docs):**
1. Update README if schema or API changed.
2. Add docs on slug collision strategy.
3. Add docs on file upload atomicity.
4. Run full test suite and Sonar scan.

---

## 6. Conclusion

| Aspect | Status | Notes |
|--------|--------|-------|
| **Issue identification** | ✅ Excellent | All four issues correctly identified with accurate root causes. |
| **Proposed solutions** | ✅ Sound | A–E are architecturally appropriate and align with LeanKernel patterns. |
| **Completeness** | ⚠️ Gaps | Missing: YAML library choice, frontmatter schema v2, collision scope, error categories, pagination details. |
| **Risky assumptions** | ⚠️ Two flagged | Caching in ResilientKnowledgeService; MCP pagination support. |
| **Test coverage** | ⚠️ Incomplete | Plan lacks specific test scenarios for collision, atomicity, and error propagation. |

**Final Verdict:** ✅ **CONDITIONAL APPROVAL** – Execute Phase 1 immediately to reduce risk, then incorporate Adjustments #1–5 into implementation spec before proceeding to Phase 2.

---

## Appendix: Files Affected

| File | Change Type | Reason |
|------|-------------|--------|
| `LeanKernel.Tools/DocumentLibraryService.cs` | Major refactor | Collision probe, two-pass write, file_upload required, cleanup |
| `LeanKernel.Tools/FrontmatterHelper.cs` (NEW) | New file | YAML serialization & parsing |
| `LeanKernel.Gateway/Services/DocumentUiService.cs` | Moderate refactor | Pagination, error categorization, frontmatter parsing |
| `LeanKernel.Gateway/Components/Pages/Knowledge.razor` | Small refactor | Use FrontmatterHelper instead of regex |
| `test/LeanKernel.Tests.Unit/Tools/DocumentLibraryServiceTests.cs` (NEW) | New file | Comprehensive coverage |
| `test/LeanKernel.Tests.Unit/Gateway/DocumentUiServiceTests.cs` (NEW) | New file | Error propagation & pagination |
| `test/LeanKernel.Tests.Unit/Tools/FrontmatterHelperTests.cs` (NEW) | New file | YAML safety |
| `LeanKernel.Tools/LeanKernel.Tools.csproj` | Minor | Add YamlDotNet dependency |
