using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools;

public sealed class DocumentIngestionHostedService(
    DocumentIngestionQueue queue,
    DocumentLibraryService libraryService,
    IServiceScopeFactory scopeFactory,
    IOptions<LeanKernelConfig> config,
    ILogger<DocumentIngestionHostedService> logger) : IHostedService
{
    private readonly DocumentIngestionQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly DocumentLibraryService _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly bool _enabled = (config ?? throw new ArgumentNullException(nameof(config))).Value.DocumentIngestion?.Enabled ?? true;
    private readonly int _maxConcurrentJobs = (config ?? throw new ArgumentNullException(nameof(config))).Value.DocumentIngestion?.MaxConcurrentJobs ?? 3;
    private readonly ILogger<DocumentIngestionHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private CancellationTokenSource? _shutdownCts;
    private Task? _runTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Document ingestion background service is disabled");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting document ingestion background service with max {MaxConcurrent} concurrent jobs", _maxConcurrentJobs);
        _shutdownCts = new CancellationTokenSource();
        _runTask = RunAsync(_shutdownCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_runTask is null || _shutdownCts is null)
        {
            return;
        }

        _logger.LogInformation("Stopping document ingestion background service with {PendingCount} pending jobs", _queue.PendingCount);

        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("CancellationTokenSource already disposed during shutdown");
            return;
        }

        try
        {
            await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Document ingestion service shutdown timed out with {PendingCount} jobs remaining", _queue.PendingCount);
        }
        finally
        {
            _shutdownCts?.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var semaphore = new SemaphoreSlim(_maxConcurrentJobs);
        var inFlight = new HashSet<Task>();

        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested)
                {
                    job.Status = DocumentIngestionStatus.Cancelled;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    continue;
                }

                inFlight.RemoveWhere(t => t.IsCompleted);

                await semaphore.WaitAsync(ct).ConfigureAwait(false);

                var task = ProcessJobAsync(job, semaphore, ct);
                inFlight.Add(task);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Document ingestion service was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document ingestion service encountered an error");
        }
        finally
        {
            semaphore.Dispose();
            if (inFlight.Count > 0)
            {
                await Task.WhenAll(inFlight).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessJobAsync(DocumentIngestionJob job, SemaphoreSlim semaphore, CancellationToken ct)
    {
        try
        {
            await PersistJobAsync(job, ct).ConfigureAwait(false);

            job.Status = DocumentIngestionStatus.Processing;
            job.StartedAt = DateTimeOffset.UtcNow;
            await PersistJobAsync(job, ct).ConfigureAwait(false);

            var result = job switch
            {
                PathDocumentIngestionJob pathJob => await _libraryService.IngestExistingDocumentAsync(
                    pathJob.SourcePath,
                    pathJob.Title,
                    pathJob.Tags,
                    ct).ConfigureAwait(false),
                { FileContent: { } fileContent } => await _libraryService.IngestDocumentAsync(
                    job.Filename,
                    fileContent,
                    job.Title,
                    job.Tags,
                    ct).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Document ingestion job {job.JobId} did not include a stream or source path.")
            };

            job.Status = DocumentIngestionStatus.Completed;
            job.Result = result;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await PersistJobAsync(job, ct).ConfigureAwait(false);

            _logger.LogInformation("Document ingestion completed: {JobId} → {PageSlug}", job.JobId, result.PageSlug);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.Status = DocumentIngestionStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await PersistJobAsync(job, ct).ConfigureAwait(false);
            _logger.LogInformation("Document ingestion was cancelled: {JobId}", job.JobId);
        }
        catch (Exception ex)
        {
            job.Status = DocumentIngestionStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await PersistJobAsync(job, ct).ConfigureAwait(false);
            _logger.LogError(ex, "Document ingestion failed: {JobId}", job.JobId);
        }
        finally
        {
            try
            {
                if (job.FileContent is not null)
                {
                    await job.FileContent.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose file stream for job {JobId}", job.JobId);
            }

            semaphore.Release();
        }
    }

    private async Task PersistJobAsync(DocumentIngestionJob job, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentIngestionJobRepository>();
            await repo.SaveJobAsync(job, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist job {JobId} with status {Status}", job.JobId, job.Status);
        }
    }
}