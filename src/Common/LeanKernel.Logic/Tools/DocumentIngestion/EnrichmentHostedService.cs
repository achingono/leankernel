using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Background service that dequeues and processes enrichment jobs.
/// Reads staged document content, extracts facts, and persists them
/// to scoped memory pages for downstream learning consumption.
/// </summary>
public sealed class EnrichmentHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<EnrichmentSettings> _settings;
    private readonly ILogger<EnrichmentHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrichmentHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory for scoped dependencies.</param>
    /// <param name="settings">The enrichment settings.</param>
    /// <param name="logger">The logger instance.</param>
    public EnrichmentHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<EnrichmentSettings> settings,
        ILogger<EnrichmentHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Enrichment hosted service started");

        await RecoverStaleLeasesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_settings.Value.Enabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IEnrichmentQueue>();

                var leaseDuration = TimeSpan.FromMinutes(_settings.Value.LeaseTimeoutMinutes > 0
                    ? _settings.Value.LeaseTimeoutMinutes
                    : 5);

                var claimed = await queue.TryClaimNextAsync(
                    Environment.MachineName,
                    leaseDuration,
                    stoppingToken);

                if (claimed == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                try
                {
                    var factService = scope.ServiceProvider.GetRequiredService<FactExtractionService>();
                    var memoryClient = scope.ServiceProvider.GetRequiredService<IMemoryClient>();

                    var documentContent = await ReadDocumentContentAsync(claimed.FilePath, stoppingToken);

                    var facts = await factService.ExtractFactsAsync(
                        null,
                        documentContent,
                        [],
                        stoppingToken);

                    if (facts.Count > 0)
                    {
                        await WriteEnrichedFactsAsync(
                            memoryClient,
                            claimed,
                            facts,
                            stoppingToken);
                    }

                    var result = new EnrichmentResult(
                        claimed.IngestionJobId,
                        claimed.Id,
                        null,
                        facts.Count > 0);

                    await queue.CompleteAsync(claimed.Id, result, stoppingToken);
                    _logger.LogInformation(
                        "Enriched document {FileName} ({Fingerprint}), extracted {FactCount} facts",
                        claimed.FileName,
                        claimed.Fingerprint,
                        facts.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enrich document {FileName}", claimed.FileName);
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
                _logger.LogError(ex, "Unexpected error in enrichment loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Enrichment hosted service stopped");
    }

    private static async Task<string> ReadDocumentContentAsync(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(filePath, ct);
    }

    private static async Task WriteEnrichedFactsAsync(
        IMemoryClient memoryClient,
        EnrichmentJobEntity job,
        IReadOnlyList<string> facts,
        CancellationToken ct)
    {
        var scope = new MemoryScope
        {
            TenantId = job.TenantId,
            PersonId = job.PersonId,
            ChannelId = job.ChannelId,
        };

        var key = $"enrichment/{job.FileName}/{DateTime.UtcNow:yyyy-MM-dd}";
        var content = string.Join("\n", facts.Select((f, i) => $"{i + 1}. {f}"));

        await memoryClient.SaveMemoryAsync(scope, key, content, ct);
    }

    private async Task RecoverStaleLeasesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IEnrichmentQueue>();
            var recovered = await queue.RecoverStaleLeasesAsync(ct);
            if (recovered > 0)
            {
                _logger.LogInformation("Recovered {Count} stale enrichment jobs with expired leases", recovered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stale enrichment lease recovery failed on startup; continuing normally");
        }
    }
}