# Useful-by-default file tools PRD

## Summary
Add built-in file-system tools, a reusable `extract_text` tool, and a richer `web_fetch` flow so agents can inspect local files and process downloaded non-text content without extra setup.

## Goals
- Add safe built-in file and directory tools under `LeanKernel.Tools/BuiltIn`.
- Add `extract_text` for plain text files and OCR-backed extraction.
- Extend `web_fetch` so binary or otherwise non-text responses are downloaded to disk and extracted.
- Keep tool behavior safe by default with path restrictions, SSRF protections, and bounded output.
- Install the OCR runtime dependency in the container image and wire the tool manifest/runtime expectations together.

## Non-goals
- No general-purpose shell execution.
- No recursive crawler or multi-page website mirroring.
- No binary output streaming from tools.

## Design
### File-system tool surface
Port the core file tools from the legacy plugin area into the current tools project:
- `directory_list`
- `directory_create`
- `file_read`
- `file_write`
- `file_edit`
- `file_copy`
- `file_move`
- `file_delete`
- `file_search`
- `file_stat`
- `file_touch`
- `file_chmod`

### Safety model
- All file tools resolve paths relative to a configured allowed root.
- Default allowed root: `/app/data`.
- Reject path traversal, symlink escapes, and access outside the allowed root.
- Write-capable tools use a tighter write allowlist for creation/mutation operations.
- File and directory tools return sanitized error messages, not raw OS path dumps.

### `extract_text`
- Tool name: `extract_text`
- Input: file path under the allowed root.
- Behavior:
  - Read UTF-8/text-like files directly.
  - For images and scanned documents, extract text using PaddleOCR.
  - Return extracted text only; do not expose scratch file paths.
- Implementation should share a reusable extraction helper with `web_fetch`.

### `web_fetch`
- Preserve the current HTTP/HTTPS allowlist and localhost/private-IP restrictions.
- Follow redirects only when the redirect target also passes URL validation.
- If the response is text-like, return the body inline as today.
- If the response is non-text, save it to a managed scratch directory under the allowed root, extract text, and return the extracted text.
- Use a size cap for downloaded bytes and a separate truncation cap for returned text.

### Container/runtime support
- Ensure the container image includes the Python runtime and PaddleOCR module required by `extract_text`.
- Keep the runtime path deterministic so the tool can invoke the OCR module directly.
- Update the tool manifest if needed so the runtime and docs match the shipped capability.

## Validation
- Add unit tests for:
  - path validation and allowlist rejection
  - file/directory listing and stat
  - write and delete safety
  - `extract_text` plain-text and OCR fallback paths
  - `web_fetch` text, binary-download, and redirect handling
- Run restore/build/test plus the repository quality checks after implementation.

## Acceptance criteria
- File and directory tools are registered in the default tool list.
- `extract_text` can return text from local files and OCR-backed content.
- `web_fetch` can handle non-text downloads and return extracted text.
- Docker image builds with the OCR dependency available.
- Tests cover the new behavior and existing tool behavior remains stable.
