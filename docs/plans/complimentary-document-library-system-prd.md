# Complementary Document Library System — Design & Implementation Plan

This document outlines the architecture, design, and step-by-step plan for building a robust **Document Library System** within LeanKernel. This system augments GBrain's markdown wiki by indexing rich files (PDF, DOCX, CSV, images) directly as specialized pages in GBrain, utilizing the engine's existing Python + OCR/text-extraction pipeline.

---

## Technical Context & Rationale

1. **The Rarchitecture Choice**: The new architecture completely removed Qdrant and Unstructured.io sidecars in favor of **GBrain** (ADR-003) and **Postgres with pgvector** (ADR-011) to simplify the operational footprint and enforce a single source of truth (ADR-004).
2. **The Gap**: GBrain is exceptionally optimized for markdown files (wiki pages). It supports uploading non-markdown assets (scanned items, images, etc.) via its file store, but it does **not** natively parse, chunk, embed, or index the textual content of these rich files for general text or hybrid/semantic searches.
3. **The Solution**: We design a **Complementary Document Library System** that converts documents into specialized GBrain pages:
   - When a document is uploaded, we leverage the engine's pre-configured `/opt/ocr-venv` Python + PaddleOCR/pdf2image environment (or direct reading for text-like files) to extract its text.
   - We compile the extracted text into a clean Markdown page with structured frontmatter (e.g., `type: document`, `title`, `source_file`, `tags`).
   - We insert this page into GBrain via `PutPageAsync` using the `doc/` slug prefix.
   - We upload the original binary using GBrain's native `file_upload` tool, linking it to the newly created page slug.
   - **Unified Search/Retrieval**: Because documents are indexed as standard GBrain pages, they instantly inherit vector embeddings, keyword indexing (`pgvector` + `tsvector`), graph linking, and citation mechanics *completely for free*. No extra databases or search systems are required!

---

## User Review Required

> [!IMPORTANT]
> - **Python + OCR Requirement**: The text extraction relies on the host/container having Python with the PaddleOCR/pdf2image stack. The LeanKernel runtime Docker image *already* configures this venv (`/opt/ocr-venv`). For local non-Docker development, the host will fall back to plain-text reading unless Python/PaddleOCR are installed on the host.
> - **Unified Namespace**: Document pages are written with the `doc/` prefix (e.g. `doc/q2-business-report`). This keeps them isolated in browsing but cleanly linkable using standard wiki-link syntax (`[[doc/q2-business-report]]`).

---

## Open Questions

> [!NOTE]
> 1. **Ingestion Timing**: Should document ingestion happen synchronously in the web request, or in a background job?
>    - *Recommendation*: Start with synchronous ingestion. If a document is exceptionally large (e.g. >100 pages), we can impose a frontend timeout or leverage a background thread.
> 2. **File Formats**: Is PDF, images, and text files sufficient for the first version, or should we incorporate library dependencies for DOCX/XLSX text parsing in C#?
>    - *Recommendation*: PDF, standard images (PNG, JPG), and plain text cover 95% of memory-indexing needs. We will focus on these first and support custom Word/Excel parsers as simple plugins.

---

## Proposed Changes

### Backend Ingestion Service

#### [NEW] [DocumentLibraryService.cs](../../src/LeanKernel.Tools/DocumentLibraryService.cs)
Create a service that handles the orchestrating pipeline of:
1. Writing the uploaded file stream to a temporary storage path under `/app/data/.scratch`.
2. Invoking the existing `TextExtractionHelper.ExtractAsync` (which automatically checks if a file is text-like or calls the Python-based OCR script for PDFs/images).
3. Slugifying the document title to create a stable key (`doc/{slug}`).
4. Compiling the frontmatter (`type: document`, `title`, `source_file`, `tags`, `imported_at`) and combining it with the extracted text to create a Markdown wiki page.
5. Putting the page into GBrain via `IKnowledgeService.PutPageAsync`.
6. Invoking the GBrain MCP `file_upload` tool using `GBrainMcpClient.CallToolAsync` to store the raw binary file in the ledger and link it directly to the page slug.
7. Returning details of the created document page.

#### [MODIFY] [KnowledgeServiceCollectionExtensions.cs](../../src/LeanKernel.Knowledge/KnowledgeServiceCollectionExtensions.cs)
Register the new `DocumentLibraryService` in the dependency injection container as a singleton:
```csharp
services.AddSingleton<DocumentLibraryService>();
```

---

### Gateway and Frontend UI

#### [NEW] [DocumentUiService.cs](../../src/LeanKernel.Gateway/Services/DocumentUiService.cs)
Create a UI bridge service that provides front-end helper methods:
- `BrowseDocumentsAsync(int pageNumber, int pageSize)`: Invokes the GBrain `list_pages` tool with `type = "document"` to return only indexed documents.
- `IngestFileAsync(string filename, Stream fileStream, string title, List<string> tags)`: Saves the stream to a temporary location and calls `DocumentLibraryService.IngestDocumentAsync`.
- `GetDownloadUrlAsync(string storagePath)`: Invokes GBrain's `file_url` tool to generate a signed download link.

#### [MODIFY] [Knowledge.razor](../../src/LeanKernel.Gateway/Components/Pages/Knowledge.razor)
We will expand the user interface in `Knowledge.razor` to introduce a premium tabbed layout (Wiki Pages vs. Document Library):
- **Wiki Pages (Tab 1)**: The standard wiki browser/search (unchanged flow, styled with a modern glassmorphism aesthetic).
- **Document Library (Tab 2)**:
  - **Left Panel (Browse Documents)**: Displays all indexed documents. Shows slug, file indicators (e.g. PDF/Image badges), and modified date. Contains a prominent "Upload document" button.
  - **Right Panel (Document Details)**: Displays the document title, original file details, associated tags, and a **Download original** button (using the signed URL). It displays the extracted text in a read-only code/markdown viewport so the user can inspect exactly what was indexed for the AI memory.
  - **Upload Modal**: A slide-in dialog containing title input, tags chip input, and a native Blazor `<InputFile>` dropzone for selecting files. Displays a progress ring during text extraction and indexing.

---

## Verification Plan

### Automated/Build Verification
- Compile and build the entire solution to ensure no syntax or registration issues:
  ```bash
  dotnet build src/LeanKernel.sln
  ```
- Run the unit/integration tests to ensure no regressions:
  ```bash
  dotnet test src/LeanKernel.sln
  ```

### Manual Verification
1. **Document Ingestion**:
   - Run the LeanKernel Gateway web interface.
   - Navigate to `/knowledge` and select the **Document Library** tab.
   - Click "Upload document", choose a PDF or image (e.g., containing some clear text), input a title and tags, and submit.
   - Observe the progress ring and ensure it successfully completes.
2. **Storage and Extraction**:
   - Verify that a new page with the prefix `doc/` is visible in the list.
   - Click on it and verify that the right-hand panel displays the exact extracted OCR text from the file.
3. **Retrieval**:
   - Navigate to the **Chat** tab and ask the AI agent a specific question about the content inside the uploaded document.
   - Verify that the agent retrieves the page `doc/your-slug` and correctly uses it as grounding context to answer your question!
