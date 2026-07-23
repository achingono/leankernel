using LeanKernel;
using LeanKernel.Events;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Events;

/// <summary>
/// Event subscriber that filters for <see cref="DocumentIngestionRequestedEvent"/>
/// and enqueues a <see cref="DocumentIngestionJob"/> to the durable queue.
/// </summary>
public sealed class DocumentIngestionSubscriber : IEventSubscriber
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentIngestionSubscriber> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentIngestionSubscriber"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="logger">The logger instance.</param>
    public DocumentIngestionSubscriber(IServiceScopeFactory scopeFactory, ILogger<DocumentIngestionSubscriber> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(IReadOnlyList<object> events, CancellationToken ct = default)
    {
        var ingestionEvents = events.OfType<DocumentIngestionRequestedEvent>().ToList();
        if (ingestionEvents.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IDocumentIngestionQueue>();

        foreach (var ev in ingestionEvents)
        {
            var job = new DocumentIngestionJob(
                ev.StagedFilePath,
                ev.FileName,
                ev.ContentType,
                ev.TenantId,
                ev.UserId,
                ev.PersonId,
                ev.ChannelId,
                ev.AvailabilityScope,
                DocumentIngestionSource.ChannelAttachment);

            var succeeded = false;
            for (var attempt = 0; attempt < 3 && !succeeded; attempt++)
            {
                try
                {
                    await queue.EnqueueAsync(job, ct);
                    succeeded = true;
                    _logger.LogDebug("Enqueued document ingestion job for attachment: {FileName}", ev.FileName);
                }
                catch (Exception ex) when (attempt < 2)
                {
                    _logger.LogWarning(ex, "Retrying enqueue for attachment: {FileName} (attempt {Attempt})", ev.FileName, attempt + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue document ingestion job for attachment: {FileName}", ev.FileName);
                }
            }
        }
    }
}
