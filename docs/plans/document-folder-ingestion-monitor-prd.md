# Document Folder Ingestion Monitor PRD

## Overview

LeanKernel currently ingests documents through explicit upload calls and queued streams. It does not monitor a filesystem folder for document imports. This change adds a configurable folder monitor so operators can bind-mount `./data/documents` into the engine container and drop files anywhere in that document tree for automatic ingestion.

## Problem Statement

Document ingestion is not useful-by-default for local document libraries because every document must be uploaded through the UI/service path. Operators need a simple Docker-friendly import path. The service must avoid re-import loops when uploaded files are stored in the same mounted document tree.

## Goals

- Add a configurable recursive document import watcher.
- Bind-mount `./data/documents` into the engine container as the user-owned drop zone.
- Monitor `/app/data/documents` by default.
- Store LeanKernel-managed upload copies in a separate named volume at `/app/data/managed-documents`.
- Import existing files without copying them back into the watched document tree.
- Keep existing UI upload ingestion behavior unchanged.
- Provide polling fallback for Docker Desktop and other environments where filesystem events may not propagate reliably.

## Non-Goals

- No persistent ingestion manifest in this slice.
- No recursive import by default.
- No deletion, moving, or archival of source files after import.
- No change to GBrain storage semantics.

## Requirements

### Functional Requirements

1. Add `LeanKernel:DocumentIngestion` settings for folder watching:
   - `WatchFolderEnabled`
   - `WatchFolderPath`
   - `WatchIncludeSubdirectories`
   - `WatchStartupScanEnabled`
   - `WatchSettleDelaySeconds`
   - `WatchPollingIntervalSeconds`
   - `WatchDefaultTags`
2. Register a hosted service that monitors the configured folder when document ingestion and folder watching are enabled.
3. Create the watch folder on startup if it does not exist.
4. Queue files from `Created` and `Renamed` events after a settle delay.
5. Poll the watch folder periodically as a fallback for bind-mounted folders that do not reliably emit events.
6. Keep startup scan disabled by default to avoid duplicate imports on every restart until persistent ingestion state exists.
7. Support path-based ingestion jobs separately from stream upload jobs.
8. Ingest existing files in place and leave source files untouched.
9. Ensure stream-upload jobs do not duplicate through the watcher by storing managed upload copies outside `/app/data/documents`.

### Non-Functional Requirements

- Log watcher and queueing failures with actionable paths and reasons.
- Avoid broad exception swallowing outside bounded event/polling workflows.
- Debounce duplicate filesystem events for the same normalized path.
- Preserve existing upload and document library tests.

## Architecture

- `DocumentIngestionConfig` owns watcher and managed storage settings.
- `DocumentFolderIngestionHostedService` owns folder monitoring, event debounce, startup directory creation, and polling fallback.
- `DocumentIngestionQueue` gains a path-job overload while retaining the existing stream-job API.
- `PathDocumentIngestionJob` identifies existing-file imports without overloading stream job semantics.
- `DocumentIngestionHostedService` dispatches stream jobs to `IngestDocumentAsync` and path jobs to `IngestExistingDocumentAsync`.
- `DocumentLibraryService.IngestDocumentAsync` writes uploaded streams to `ManagedStoragePath`, which is not monitored by the folder watcher.
- `DocumentLibraryService.IngestExistingDocumentAsync` validates the source file is under `AllowedRoot/documents`, extracts text, writes the wiki page, uploads the existing binary to GBrain, and does not copy/delete the source file.

## Configuration

Default application configuration:

```json
"DocumentIngestion": {
  "Enabled": true,
  "MaxConcurrentJobs": 3,
  "MaxQueuedDocuments": 100,
  "WatchFolderEnabled": true,
  "WatchFolderPath": "/app/data/documents",
  "WatchIncludeSubdirectories": true,
  "WatchStartupScanEnabled": false,
  "WatchSettleDelaySeconds": 2,
  "WatchPollingIntervalSeconds": 30,
  "WatchDefaultTags": [ "auto-import" ],
  "ManagedStoragePath": "/app/data/managed-documents"
}
```

Docker Compose mounts the host document tree into the container:

```yaml
volumes:
  - ./data/documents:/app/data/documents
  - document-managed-data:/app/data/managed-documents
```

Operators should drop files anywhere under `./data/documents` for automatic import. Uploaded files managed by LeanKernel are stored in the named `document-managed-data` volume and are intentionally not visible in the user drop-zone bind mount.

## Risks and Mitigations

- **Duplicate imports on restart:** Startup scan remains disabled by default until persistent imported-file state exists.
- **Docker Desktop event gaps:** Polling fallback scans for files not already queued in memory.
- **Upload import loops:** The watcher monitors only the user-owned document bind mount. Managed upload copies are stored in a separate named volume.
- **Recursive permissions:** Recursive polling uses inaccessible-directory skipping so one unreadable folder does not abort the full scan.
- **Partially copied files:** A settle delay waits for file size and timestamp stability before queueing.

## Acceptance Criteria

- `./data/documents` is bind-mounted to `/app/data/documents` for the engine service.
- `document-managed-data` is mounted to `/app/data/managed-documents` for service-owned upload copies.
- The configured watcher path is `/app/data/documents`.
- Recursive watching is enabled by default for the Docker runtime.
- Existing uploads still ingest successfully.
- Path-based jobs ingest existing files without deleting source files.
- The watcher is registered and gated by config.
- Unit tests cover path queueing and in-place document ingestion.
- Build, tests, coverage, Sonar, and Docker Compose build validation are run.
