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
| `AttachmentIngestionMiddleware` | `Gateway/Providers/` | Intercepts multipart/form-data requests, stages files, enforces tenant-scope badge guard, and emits `DocumentIngestionRequestedEvent` |
| `DocumentUploadEndpoint` | `Gateway/Requests/` | `POST /api/documents/upload` endpoint for authenticated upload staging and queueing |
| `PersistEventSubscriber` | `Logic/Events/` | Writes collected events to `IEventStore` at flush time |
| `DocumentIngestionSubscriber` | `Logic/Events/` | Filters `DocumentIngestionRequestedEvent` and enqueues jobs to the durable queue |

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

| Extension | Method |
|---|---|
| `.txt`, `.md`, `.csv`, `.json`, `.xml`, `.html`, `.yaml`, `.yml` | `File.ReadAllTextAsync` |
| `.pdf` | Basic stream read (no OCR; returns empty for PDF headers) |
| Other | Returns empty string |

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
