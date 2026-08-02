# Document Ingestion

LeanKernel implements channel-aware document ingestion as a background pipeline that ingests channel attachments, upload endpoint files, and watch-folder files into the document library, with deduplication, retry, scope-aware storage, and policy-enforced search.

## Architecture

```
Attachment/file → EventIngestion/Subscriber → DocumentIngestionQueue → DocumentIngestionHostedService → DocumentLibraryService → IDocumentStoreClient
                                                                                                                         → Disk storage
```

### Components

| Component | Location | Purpose |
|---|---|---|
| `DocumentIngestionQueue` | `Logic/Tools/DocumentIngestion/` | Durable EF-backed queue with status tracking, lease claims, retry scheduling, and stale-lease recovery |
| `DocumentIngestionHostedService` | `Logic/Tools/DocumentIngestion/` | Background service that dequeues jobs, calls `DocumentLibraryService`, and completes/fails jobs |
| `WatchFolderHostedService` | `Logic/Tools/DocumentIngestion/` | Background service that monitors configured watch folders using `FileSystemWatcher` and enqueues new files |
| `DocumentLibraryService` | `Logic/Tools/DocumentIngestion/` | Computes SHA-256 fingerprints, copies files to hierarchical storage, extracts text, and upserts to the document catalog |
| `AttachmentIngestionMiddleware` | `Gateway/Providers/` | Intercepts multipart/form-data requests, channel JSON attachment envelopes (`channel_attachments`), and OpenAI-compatible content parts (`messages[].content` for chat completions, `input[].content` for responses), stages files, extracts text inline and rewrites the forwarded body so the model sees the document text, enforces tenant-scope badge guard, and emits `DocumentIngestionRequestedEvent` |
| `DocumentUploadEndpoint` | `Gateway/Requests/` | `POST /api/documents/upload` endpoint for authenticated upload staging and queueing |
| `PersistEventSubscriber` | `Logic/Events/` | Writes collected events to `IEventStore` at flush time |
| `DocumentIngestionSubscriber` | `Logic/Events/` | Filters `DocumentIngestionRequestedEvent` and enqueues jobs to the durable queue |

## Attachment Shapes Intercepted

The gateway middleware stages files for ingestion from three request shapes:

1. **multipart/form-data** uploads with files.
2. **Channel JSON envelopes**: a root-level `channel_attachments` (or `channelAttachments`) array where each item carries `contentType`/`content_type`, `fileName`/`file_name`, and `fileDataUrl`/`file_data_url` (a `data:` URL).
3. **OpenAI-compatible content parts**: chat completions `messages[].content` and responses `input[].content` arrays. Data URLs are extracted from `image_url` parts (`{ "url": "data:..." }` or a bare string), `file` parts (`{ "file": { "file_data": "data:...", "filename": "..." } }`), and flat `file_data`/`fileData`/`data`/`url` properties on a part. `input_file` parts that only carry a `file_id` reference are skipped (no server-side file store to resolve them).

In all shapes, image content types are excluded from document ingestion (images remain inline for multimodal understanding), malformed data URLs are skipped, and `Files:MaxDownloadBytes` is enforced per attachment.

## Queue Semantics

| Status | Meaning | Transition |
|---|---|---|
| `Pending` | Awaiting processing | `EnqueueAsync`, `FailAsync` (retry), `RecoverStaleLeasesAsync` |
| `Processing` | Claimed by a worker | `TryClaimNextAsync` |
| `Completed` | Successfully ingested | `CompleteAsync` (Success=true) |
| `Failed` | Processing completed with error | `CompleteAsync` (Success=false) |
| `Poisoned` | Retry budget exhausted | `FailAsync` (attempts >= 5) |

- Leases auto-expire after the configured duration; stale leases are recovered at startup via `RecoverStaleLeasesAsync`.
- Retry uses exponential backoff: `nextAttempt = now + 2^(attemptCount+1) minutes`.

## Storage Layout

All ingested files are stored under `{Files:RootPath}/documents/` using a hierarchical path:

```
{RootPath}/documents/{TenantId}/{Scope}/{ChannelId}/{UserId}/{fingerprint[0..2]}/{fingerprint[2..4]}/{FileName}
```

## Text Extraction

`DocumentLibraryService` extracts text when ingesting a document into the catalog. Office Open XML, EPUB, and legacy binary Office formats are routed through `TextExtractionHelper` (Python-stdlib `zipfile` + `ElementTree`). Extraction failures (missing Python, invalid archives) are caught, logged, and produce an empty `extractedText` so ingestion still succeeds.

| Extension | Method |
|---|---|
| `.txt`, `.md`, `.csv`, `.json`, `.xml`, `.html`, `.yaml`, `.yml` | `File.ReadAllTextAsync` |
| `.pdf` | Basic stream read (no OCR; returns empty for PDF headers) |
| `.epub` | `TextExtractionHelper` via Python stdlib |
| `.docx`, `.docm`, `.dotx`, `.dotm` | `TextExtractionHelper` via Python stdlib |
| `.xlsx`, `.xlsm`, `.xltx`, `.xltm` | `TextExtractionHelper` via Python stdlib |
| `.pptx`, `.pptm`, `.ppsx`, `.ppsm`, `.potx`, `.potm` | `TextExtractionHelper` via Python stdlib |
| `.doc`, `.xls`, `.ppt` | `TextExtractionHelper` legacy binary printable-string extraction |
| Other | Returns empty string |

## Inline Text Injection for OpenAI Content Parts

When a request carries document content parts (`messages[].content` chat completions or `input[].content` responses), the gateway stages each non-image attachment and, before forwarding the request downstream, injects the extracted text inline so the model can answer from the document directly:

- Successfully extracted, non-truncated content replaces the data-URL part with a `{ "type": "text" }` (chat completions) or `{ "type": "input_text" }` (responses) part formatted as `[Attached file: {name}]\n{extractedText}`.
- Empty or truncated extraction (at/beyond `Files:MaxExtractedCharacters`, default 20,000) injects a notification part instead: `Attached document "{name}" was uploaded to the document library. Ingestion is in progress — use the document_search tool to retrieve it after ingestion completes.`
- `image/*` parts are never replaced and pass through unchanged for vision models.
- PDF content is not extracted inline (no OCR in the gateway image); it always falls back to the notification flow.
- The rewritten body preserves sibling properties, non-matching parts, and message ordering; the message content string `data:` URL edge case is replaced with a single-part content array.

Inline injection is additive to the request only — the asynchronous ingestion pipeline (event emission → durable queue → hosted service → `DocumentLibraryService`) is unchanged.

## Tools

| Tool | Description | Parameters |
|---|---|---|
| `document_search` | Search ingested documents by query text | `query` (required), `channelIds` (optional), `maxResults` (default 10) |
| `document_list` | List ingested documents | `channelIds` (optional), `limit` (default 50) |

Both tools enforce channel visibility via `IChannelMemoryPolicyResolver`:
- Without explicit `channelIds`: results scoped to all readable channels
- With explicit `channelIds`: each channel must be in the caller's readable set

## Upload Endpoint

`POST /api/documents/upload`:

- requires authentication
- requires `channel_id` and a non-empty `file`
- validates requested channel against readable-channel policy
- defaults `availability_scope` to `user`
- rejects tenant scope when caller badge identity is empty
- stages file under `{Files:RootPath}/documents/.../_staging`
- enqueues `DocumentIngestionJob` and returns `202 Accepted`

## Configuration

```json
{
  "Agents:Tools:DocumentIngestion": {
    "Enabled": false,
    "MaxConcurrentJobs": 3,
    "QueueCapacity": 100,
    "EnqueueTimeoutSeconds": 30,
    "WatchSettleDelaySeconds": 2,
    "WatchMaxRetries": 3,
    "WatchRetryBaseDelaySeconds": 1,
    "WatchRetryMaxDelaySeconds": 60
  },
  "Files:WatchFolders": [
    {
      "Path": "/data/watch/inbox",
      "FilePattern": "*.*",
      "TenantId": "...",
      "UserId": "...",
      "PersonId": "...",
      "ChannelId": "...",
      "AvailabilityScope": "User"
    }
  ]
}
```

Validated at startup via `ValidateOnStart()`.

## Key Contracts

- `IDocumentStoreClient` (`Logic/Providers/`): Provider-agnostic document catalog abstraction. Implemented by `GBrainDocumentStoreClient` in the Gateway.
- `IEventSubscriber` (`Logic/Events/`): Flush-time dispatch contract. Multiple subscribers receive the same batched events.
- `IHasEnvelope` (`Core/`): Marker interface for generic envelope resolution.
- `DocumentIngestionRequestedEvent` (`Core/Events/`): Event fired when a channel attachment is staged for ingestion.
