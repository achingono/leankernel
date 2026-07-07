using System.Collections.Concurrent;
using System.Threading.Channels;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Tools;

public sealed class DocumentIngestionQueue : IDocumentIngestionQueue
{
    private readonly Channel<DocumentIngestionJob> _channel;
    private readonly ConcurrentDictionary<string, DocumentIngestionJob> _jobs = new(StringComparer.Ordinal);
    private readonly int _enqueueTimeoutMs;
    private readonly int _maxCapacity;

    public DocumentIngestionQueue(int maxQueuedJobs = 100, int enqueueTimeoutMs = 30_000)
    {
        _maxCapacity = Math.Max(1, maxQueuedJobs);
        _enqueueTimeoutMs = Math.Max(100, enqueueTimeoutMs);
        var options = new BoundedChannelOptions(_maxCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _channel = Channel.CreateBounded<DocumentIngestionJob>(options);
    }

    public DocumentIngestionJob Queue(
        string filename,
        Stream fileContent,
        string? title,
        List<string> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentNullException.ThrowIfNull(fileContent);
        ArgumentNullException.ThrowIfNull(tags);

        var job = new DocumentIngestionJob
        {
            Filename = filename,
            FileContent = fileContent,
            Title = title,
            Tags = tags,
            Status = DocumentIngestionStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return EnqueueSync(job);
    }

    public PathDocumentIngestionJob QueuePath(
        string sourcePath,
        string? title,
        List<string> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(tags);

        var job = new PathDocumentIngestionJob
        {
            Filename = Path.GetFileName(sourcePath),
            SourcePath = sourcePath,
            Title = title,
            Tags = tags,
            Status = DocumentIngestionStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return (PathDocumentIngestionJob)EnqueueSync(job);
    }

    public async Task<EnqueueResult> QueueAsync(
        string filename,
        Stream fileContent,
        string? title,
        List<string> tags,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentNullException.ThrowIfNull(fileContent);
        ArgumentNullException.ThrowIfNull(tags);

        var job = new DocumentIngestionJob
        {
            Filename = filename,
            FileContent = fileContent,
            Title = title,
            Tags = tags,
            Status = DocumentIngestionStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return await EnqueueAsync(job, ct).ConfigureAwait(false);
    }

    public async Task<EnqueueResult> QueuePathAsync(
        string sourcePath,
        string? title,
        List<string> tags,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(tags);

        var job = new PathDocumentIngestionJob
        {
            Filename = Path.GetFileName(sourcePath),
            SourcePath = sourcePath,
            Title = title,
            Tags = tags,
            Status = DocumentIngestionStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return await EnqueueAsync(job, ct).ConfigureAwait(false);
    }

    public DocumentIngestionJob? GetJobStatus(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public int PendingCount => _channel.Reader.Count;

    internal ChannelReader<DocumentIngestionJob> Reader => _channel.Reader;

    private DocumentIngestionJob EnqueueSync(DocumentIngestionJob job)
    {
        _jobs.TryAdd(job.JobId, job);
        if (_channel.Writer.TryWrite(job))
        {
            return job;
        }

        _jobs.TryRemove(job.JobId, out _);

        // TryWrite can fail for two reasons: (a) the channel is full, or (b) the writer
        // has been completed/faulted (e.g. during shutdown). Check completion state to
        // provide an accurate diagnostic message rather than blaming capacity.
        if (_channel.Reader.Completion.IsCompleted)
        {
            var completion = _channel.Reader.Completion;
            var reason = completion.Exception?.InnerException?.Message
                ?? "Channel writer has been completed";
            throw new InvalidOperationException(
                $"Document ingestion queue is closed: {reason}");
        }

        throw new InvalidOperationException(
            $"Document ingestion queue is full. Max capacity: {_maxCapacity}");
    }

    private async Task<EnqueueResult> EnqueueAsync(DocumentIngestionJob job, CancellationToken ct)
    {
        _jobs.TryAdd(job.JobId, job);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_enqueueTimeoutMs);

            await _channel.Writer.WriteAsync(job, timeoutCts.Token).ConfigureAwait(false);
            return new EnqueueResult(job, EnqueueOutcome.Queued);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _jobs.TryRemove(job.JobId, out _);
            return new EnqueueResult(null, EnqueueOutcome.TimedOut, "Queue write timed out");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _jobs.TryRemove(job.JobId, out _);
            return new EnqueueResult(null, EnqueueOutcome.Cancelled, "Enqueue was cancelled");
        }
        catch (Exception ex)
        {
            _jobs.TryRemove(job.JobId, out _);
            return new EnqueueResult(null, EnqueueOutcome.Rejected, ex.Message);
        }
    }
}