# Enrichment Pipeline

The enrichment pipeline extends document ingestion with post-ingestion processing that
extracts facts and emits durable enrichment events.

## Flow

1. `DocumentIngestionHostedService` completes ingestion for a claimed queue item.
2. When `Agents:Tools:DocumentIngestion:EnrichmentEnabled` is true and ingestion succeeds,
   the service enqueues an `EnrichmentJob`.
3. The same path appends `DocumentEnrichmentRequestedEvent` to the event spine.
4. `EnrichmentHostedService` claims enrichment jobs and runs fact extraction.

## Components

- `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/DocumentIngestionHostedService.cs`
- `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/EnrichmentQueue.cs`
- `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/EnrichmentHostedService.cs`
- `src/Common/LeanKernel.Core/Events/DocumentEnrichmentRequestedEvent.cs`
- `src/Common/LeanKernel.Core/Entities/EnrichmentJobEntity.cs`

## Configuration

- `Agents:Tools:DocumentIngestion:EnrichmentEnabled`
- `Agents:Tools:DocumentIngestion:Enrichment:Enabled`
- `Agents:Tools:DocumentIngestion:Enrichment:MaxConcurrentJobs`
- `Agents:Tools:DocumentIngestion:Enrichment:QueueCapacity`
- `Agents:Tools:DocumentIngestion:Enrichment:LeaseTimeoutMinutes`
