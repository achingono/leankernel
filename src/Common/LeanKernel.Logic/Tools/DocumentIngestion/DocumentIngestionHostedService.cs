using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Events;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Background service that dequeues and processes document ingestion jobs.
/// </summary>
public sealed class DocumentIngestionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DocumentIngestionToolSettings> _settings;
    private readonly ILogger<DocumentIngestionHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentIngestionHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory for scoped dependencies.</param>
    /// <param name="settings">The document ingestion tool settings.</param>
    /// <param name="logger">The logger instance.</param>
    public DocumentIngestionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DocumentIngestionToolSettings> settings,
        ILogger<DocumentIngestionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document ingestion hosted service started");

        await RecoverStaleLeasesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IDocumentIngestionQueue>();
                var library = scope.ServiceProvider.GetRequiredService<IDocumentLibraryService>();

                var leaseDuration = TimeSpan.FromMinutes(_settings.Value.EnqueueTimeoutSeconds > 0
                    ? _settings.Value.EnqueueTimeoutSeconds
                    : 300);

                var claimed = await queue.TryClaimNextAsync(
                    Environment.MachineName,
                    leaseDuration,
                    stoppingToken);

                if (claimed == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var source = Enum.Parse<DocumentIngestionSource>(claimed.Source);
                var availabilityScope = Enum.Parse<DocumentAvailabilityScope>(claimed.AvailabilityScope);
                var job = new DocumentIngestionJob(
                    claimed.FilePath,
                    claimed.FileName,
                    claimed.ContentType,
                    claimed.TenantId,
                    claimed.UserId,
                    claimed.PersonId,
                    claimed.ChannelId,
                    availabilityScope,
                    source);

                try
                {
                    var result = await library.IngestDocumentAsync(job, stoppingToken);
                    await queue.CompleteAsync(claimed.Id, result, stoppingToken);

                    if (_settings.Value.EnrichmentEnabled && result.Success && !result.IsDuplicate)
                    {
                        await EnqueueEnrichmentAsync(scope.ServiceProvider, claimed, result, stoppingToken);
                    }

                    _logger.LogInformation("Ingested document {FileName} ({Fingerprint})", claimed.FileName, result.Fingerprint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to ingest document {FileName}", claimed.FileName);
                    var retryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, claimed.AttemptCount + 1));
                    await queue.FailAsync(claimed.Id, ex.Message, retryAt, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in document ingestion loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Document ingestion hosted service stopped");
    }

    private async Task RecoverStaleLeasesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IDocumentIngestionQueue>();
            var recovered = await queue.RecoverStaleLeasesAsync(ct);
            if (recovered > 0)
            {
                _logger.LogInformation("Recovered {Count} stale ingestion jobs with expired leases", recovered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stale lease recovery failed on startup; continuing normally");
        }
    }

    private async Task EnqueueEnrichmentAsync(
        IServiceProvider serviceProvider,
        DocumentIngestionJobEntity claimed,
        IngestionResult result,
        CancellationToken ct)
    {
        var enrichmentQueue = serviceProvider.GetService<IEnrichmentQueue>();
        if (enrichmentQueue is null)
        {
            _logger.LogDebug("Enrichment is enabled but IEnrichmentQueue is not registered. Skipping enrichment enqueue.");
            return;
        }

        var source = Enum.Parse<DocumentIngestionSource>(claimed.Source);
        var availabilityScope = Enum.Parse<DocumentAvailabilityScope>(claimed.AvailabilityScope);
        var enrichmentJobId = Guid.NewGuid();

        await enrichmentQueue.EnqueueAsync(
            new EnrichmentJob(
                enrichmentJobId,
                claimed.Id,
                claimed.FilePath,
                claimed.FileName,
                result.Fingerprint,
                claimed.TenantId,
                claimed.UserId,
                claimed.PersonId,
                claimed.ChannelId,
                availabilityScope,
                source),
            ct);

        var eventStore = serviceProvider.GetService<IEventStore>();
        if (eventStore is null)
        {
            _logger.LogDebug("IEventStore is not registered. Skipping DocumentEnrichmentRequestedEvent append.");
            return;
        }

        var envelope = new EventEnvelope
        {
            EventType = "document_enrichment_requested",
            TenantId = claimed.TenantId,
            PersonId = claimed.PersonId,
            UserId = claimed.UserId,
            ChannelId = claimed.ChannelId,
            CorrelationId = claimed.Id.ToString("N"),
        };

        await eventStore.AppendAsync(
            new DocumentEnrichmentRequestedEvent
            {
                Envelope = envelope,
                IngestionJobId = claimed.Id,
                TenantId = claimed.TenantId,
                UserId = claimed.UserId,
                PersonId = claimed.PersonId,
                ChannelId = claimed.ChannelId,
            },
            ct);
    }
}